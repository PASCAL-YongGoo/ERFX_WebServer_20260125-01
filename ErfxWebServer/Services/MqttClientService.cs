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
    private IMqttClient? _mqttClient;
    private bool _disposed;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;

    public MqttClientService(
        ILogger<MqttClientService> logger,
        IHubContext<InspectionHub> hubContext,
        IConfiguration configuration)
    {
        _logger = logger;
        _hubContext = hubContext;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var brokerHost = _configuration["Mqtt:BrokerHost"] ?? "localhost";
        var brokerPort = int.Parse(_configuration["Mqtt:BrokerPort"] ?? "1883");
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
                    var brokerPort = int.Parse(_configuration["Mqtt:BrokerPort"] ?? "1883");
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

        _logger.LogDebug("Received message on {Topic}: {Payload}", topic, payload);

        try
        {
            // 검사 결과 JSON을 파싱하여 BoxInspectionResult로 변환
            var result = JsonSerializer.Deserialize<BoxInspectionResult>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result != null)
            {
                // SignalR로 파싱된 결과 전송
                await _hubContext.Clients.All.SendAsync("InspectionResult", result);
                _logger.LogInformation("Inspection result received: {InvoiceNumber} - {Result}", 
                    result.InvoiceNumber, result.Result);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse inspection result JSON");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
        _mqttClient?.Dispose();
    }
}
