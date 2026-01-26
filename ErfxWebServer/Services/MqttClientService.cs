using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using MQTTnet;
using MQTTnet.Protocol;
using ErfxWebServer.Hubs;
using ErfxWebServer.Models;

namespace ErfxWebServer.Services;

/// <summary>
/// MQTT 클라이언트 서비스 - Inspector 토픽 구독 및 SignalR 브로드캐스트
/// MQTTnet v5 API 사용
/// </summary>
public class MqttClientService : IHostedService, IDisposable
{
    private readonly ILogger<MqttClientService> _logger;
    private readonly IHubContext<InspectionHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private IMqttClient? _mqttClient;
    private System.Threading.Timer? _statusTimer;
    private bool _disposed;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;

    public MqttClientService(
        ILogger<MqttClientService> logger,
        IHubContext<InspectionHub> hubContext,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _hubContext = hubContext;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var brokerHost = _configuration["Mqtt:BrokerHost"] ?? "localhost";
        var brokerPort = int.TryParse(_configuration["Mqtt:BrokerPort"], out var port) ? port : 1883;
        var clientId = _configuration["Mqtt:ClientId"] ?? "ErfxWebServer";

        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId($"{clientId}_{Guid.NewGuid():N}")
            .WithCleanSession()
            .Build();

        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        try
        {
            _logger.LogInformation("Connecting to MQTT broker at {Host}:{Port}...", brokerHost, brokerPort);
            await _mqttClient.ConnectAsync(options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to MQTT broker. Will retry on reconnect.");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("Connected to MQTT broker");

        // 토픽 구독
        var resultTopic = _configuration["Mqtt:Topics:InspectionResult"] ?? "erfx/inspector/result";

        await _mqttClient!.SubscribeAsync(resultTopic, MqttQualityOfServiceLevel.AtLeastOnce);
        _logger.LogInformation("Subscribed to topic: {Topic}", resultTopic);

        // 연결 상태 브로드캐스트
        await _hubContext.Clients.All.SendAsync("MqttConnected", true);

        // 상태 발행 타이머 시작
        StartStatusPublishTimer();
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("Disconnected from MQTT broker: {Reason}", e.Reason);

        await _hubContext.Clients.All.SendAsync("MqttConnected", false);

        // 자동 재연결 (5초 대기)
        if (!_disposed)
        {
            await Task.Delay(5000);
            try
            {
                if (_mqttClient != null && !_mqttClient.IsConnected)
                {
                    var brokerHost = _configuration["Mqtt:BrokerHost"] ?? "localhost";
                    var brokerPort = int.TryParse(_configuration["Mqtt:BrokerPort"], out var port) ? port : 1883;
                    var clientId = _configuration["Mqtt:ClientId"] ?? "ErfxWebServer";

                    var options = new MqttClientOptionsBuilder()
                        .WithTcpServer(brokerHost, brokerPort)
                        .WithClientId($"{clientId}_{Guid.NewGuid():N}")
                        .WithCleanSession()
                        .Build();

                    await _mqttClient.ConnectAsync(options);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection failed. Will retry...");
            }
        }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

        _logger.LogDebug("Received message on {Topic}: {PayloadLength} bytes", topic, payload.Length);

        try
        {
            var result = JsonSerializer.Deserialize<BoxInspectionResult>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result != null)
            {
                await SaveToDatabaseAsync(result);
                await _hubContext.Clients.All.SendAsync("InspectionResult", result);
                _logger.LogInformation("Inspection result saved and broadcast: {InvoiceNumber} - {Result}", 
                    result.InvoiceNumber, result.Result);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse inspection result JSON");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inspection result");
        }
    }

    private async Task SaveToDatabaseAsync(BoxInspectionResult result)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInspectionService>();

        if (!string.IsNullOrEmpty(result.CorrelationId))
        {
            var exists = await service.ExistsByCorrelationIdAsync(result.CorrelationId);
            if (exists)
            {
                _logger.LogDebug("Skipping duplicate: {CorrelationId}", result.CorrelationId);
                return;
            }
        }

        await service.SaveAsync(result);
    }

    private void StartStatusPublishTimer()
    {
        var intervalMs = int.TryParse(_configuration["Mqtt:StatusPublish:IntervalMs"], out var parsed)
            ? parsed : 30000;
        var enabled = bool.TryParse(_configuration["Mqtt:StatusPublish:Enabled"], out var isEnabled)
            ? isEnabled : true;

        if (!enabled)
        {
            _logger.LogInformation("Status publishing is disabled");
            return;
        }

        _statusTimer?.Dispose();
        _statusTimer = new System.Threading.Timer(
            async _ =>
            {
                try
                {
                    await PublishStatusAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Status publish failed in timer callback");
                }
            },
            null,
            TimeSpan.FromMilliseconds(1000), // 첫 발행은 1초 후
            TimeSpan.FromMilliseconds(intervalMs)
        );

        _logger.LogInformation("Status publishing started (interval: {IntervalMs}ms)", intervalMs);
    }

    private async Task PublishStatusAsync()
    {
        if (_mqttClient?.IsConnected != true)
            return;

        try
        {
            var statusTopic = _configuration["Mqtt:StatusPublish:Topic"] ?? "erfx/webserver/status";

            var status = new
            {
                source = "WebServer",
                type = "webserver_status",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                payload = new
                {
                    running = true,
                    connected = _mqttClient.IsConnected,
                    version = "1.0",
                    uptime = (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds
                }
            };

            var json = JsonSerializer.Serialize(status);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(statusTopic)
                .WithPayload(json)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _mqttClient.PublishAsync(message);
            _logger.LogDebug("Published status to {Topic}", statusTopic);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish status");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _statusTimer?.Dispose();
        _statusTimer = null;

        if (_mqttClient?.IsConnected == true)
        {
            await _mqttClient.DisconnectAsync();
        }
        _logger.LogInformation("MQTT client stopped");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _statusTimer?.Dispose();
        _mqttClient?.Dispose();
    }
}
