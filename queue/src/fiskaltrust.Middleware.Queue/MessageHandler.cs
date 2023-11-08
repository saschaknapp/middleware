using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using fiskaltrust.ifPOS.v1;
using fiskaltrust.Middleware.Contracts.Models;
using fiskaltrust.Middleware.Contracts.Repositories;
using fiskaltrust.storage.V0;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Newtonsoft.Json;

namespace fiskaltrust.Middleware.Queue
{

    public record SignRequestMessage(string operationId, int lifetime, ReceiptRequest request);

    public record SignRequestAcceptedMessage(string operationId, Guid queueId, Guid queueItemId);

    public record SignRequestDoneMessage(ReceiptResponse response);

    public record SignRequestStateMessage(string operationId, Guid queueId, Guid? queueItemId, string state, string stateMessage, string stateData);

    public class MessageHandler
    {
        private IManagedMqttClient _mqttClient;
        private readonly ILogger<MessageHandler> _logger;
        private Guid _queueId;
        private readonly SignProcessorV2 _signProcessorV2;
        private readonly IMiddlewareQueueItemRepository _queueItemRepository;
        private Guid _cashBoxId;
        private readonly string _stateRequestTopic;
        private readonly string _signRequestTopic;
        private readonly string _signResponseTopic;

        public Dictionary<string, SignRequestStateMessage> Results { get; set; } = new Dictionary<string, SignRequestStateMessage>();

        public MessageHandler(ILogger<MessageHandler> logger, Guid queueId, MiddlewareConfiguration configuration, SignProcessorV2 signProcessorV2, IMiddlewareQueueItemRepository queueItemRepository) 
        {
            _logger = logger;
            _queueId = queueId;
            _signProcessorV2 = signProcessorV2;
            _queueItemRepository = queueItemRepository;
            _cashBoxId = configuration.CashBoxId;
            _stateRequestTopic = $"{_cashBoxId}/signrequest/state";
            _signRequestTopic = $"{_cashBoxId}/signrequest";
            _signResponseTopic = $"{_cashBoxId}/signresponse/#";
        }

        public async Task RegisterMQTTBusAsync()
        {
            var mqttFactory = new MqttFactory();
            var clientId = _queueId;
            _mqttClient = mqttFactory.CreateManagedMqttClient();
            var mqttClientOptions = new MqttClientOptionsBuilder()
                    .WithClientId(clientId.ToString())
                    .WithCleanSession(false)
                    .WithoutThrowOnNonSuccessfulConnectResponse()
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                    .WithWebSocketServer(o => o.WithUri("gateway-sandbox.fiskaltrust.eu:80/mqtt"))
                    .Build();

            var managedMqttClientOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(mqttClientOptions)
                .Build();

            await _mqttClient.StartAsync(managedMqttClientOptions);

            _mqttClient.ApplicationMessageReceivedAsync += HandleMQTTMessage;

            await _mqttClient.SubscribeAsync(new List<MqttTopicFilter>
            {
                new MqttTopicFilter
                {
                    Topic = _stateRequestTopic
                },
                new MqttTopicFilter
                {
                    Topic = _signRequestTopic
                },
                new MqttTopicFilter
                {
                    Topic = _signResponseTopic
                }
            });
        }

        private async Task HandleMQTTMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Received new message for topic {topic} with responsetopic {responsetopic}", e.ApplicationMessage.Topic, e.ApplicationMessage.ResponseTopic);
                if (e.ApplicationMessage.Topic == _stateRequestTopic)
                {
                    var ss = new MqttApplicationMessageBuilder()
                        .WithTopic(e.ApplicationMessage.ResponseTopic)
                        .WithPayload(JsonConvert.SerializeObject(Results.Values.ToList()))
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                        .Build();
                    await _mqttClient.EnqueueAsync(ss);
                    await e.AcknowledgeAsync(CancellationToken.None);
                    _logger.LogDebug("Published new message to {topic}", e.ApplicationMessage.ResponseTopic);
                }
                else
                {
                    var signRequestMessage = JsonConvert.DeserializeObject<SignRequestMessage>(e.ApplicationMessage.ConvertPayloadToString());
                    OperationalQueueItem queueItem = null;
                    if (!Results.ContainsKey(signRequestMessage.operationId))
                    {
                        queueItem = await _signProcessorV2.QueueQueueItemAsync(signRequestMessage.request, signRequestMessage.operationId);
                        Results.Add(signRequestMessage.operationId, new SignRequestStateMessage(signRequestMessage.operationId, _queueId, queueItem.ftQueueItemId, "Pending", null, null));       
                    }
                    else
                    {
                        var storedQueueItem = await _queueItemRepository.GetAsync(Results[signRequestMessage.operationId].queueId);
                        queueItem = new OperationalQueueItem
                        {
                            ftDoneMoment = storedQueueItem.ftDoneMoment,
                            ftQueueId = storedQueueItem.ftQueueId,  
                            ftQueueItemId = storedQueueItem.ftQueueItemId,
                            cbReceiptMoment = storedQueueItem.cbReceiptMoment,
                            cbReceiptReference = storedQueueItem.cbReceiptReference,
                            cbTerminalID = storedQueueItem.cbTerminalID,
                            country = storedQueueItem.country,
                            ftQueueMoment = storedQueueItem.ftQueueMoment,
                            ftQueueRow = storedQueueItem.ftQueueRow,
                            ftQueueTimeout = storedQueueItem.ftQueueTimeout,
                            ftWorkMoment = storedQueueItem.ftWorkMoment,
                            OperationId = signRequestMessage.operationId,
                            request = storedQueueItem.request,
                            requestHash = storedQueueItem.requestHash,
                            response = storedQueueItem.response,
                            responseHash = storedQueueItem.responseHash,
                            TimeStamp = storedQueueItem.TimeStamp,
                            version = storedQueueItem.version
                        };
                        // TODO we need to check if it was processed already and react accordingly
                    }

                    await _mqttClient.EnqueueAsync(e.ApplicationMessage.ResponseTopic, JsonConvert.SerializeObject(new SignRequestAcceptedMessage(queueItem.OperationId, queueItem.ftQueueId, queueItem.ftQueueItemId)), MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
                    _logger.LogDebug("Published new message to {topic}", e.ApplicationMessage.ResponseTopic);
                    await e.AcknowledgeAsync(CancellationToken.None);

                    Results[queueItem.OperationId] = new SignRequestStateMessage(queueItem.OperationId, queueItem.ftQueueId, queueItem.ftQueueItemId, "Processing", null, null);
                    var receiptResponse = await _signProcessorV2.ProcessQueueItemAsync(queueItem);
                    Results[queueItem.OperationId] = new SignRequestStateMessage(queueItem.OperationId, queueItem.ftQueueId, queueItem.ftQueueItemId, "Done", null, JsonConvert.SerializeObject(receiptResponse));
                    var topic = $"{_cashBoxId}/signrequest/{queueItem.OperationId}/done";
                    await _mqttClient.EnqueueAsync(topic, JsonConvert.SerializeObject(new SignRequestDoneMessage(receiptResponse)), MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
                    _logger.LogDebug("Published new message to {topic}", topic);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message for topic  {topic}", e.ApplicationMessage.Topic);
                throw;
            }
        }
    }
}