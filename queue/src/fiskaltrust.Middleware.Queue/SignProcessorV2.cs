using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using fiskaltrust.ifPOS.v1;
using fiskaltrust.Middleware.Contracts.Extensions;
using fiskaltrust.Middleware.Contracts.Interfaces;
using fiskaltrust.Middleware.Contracts.Models;
using fiskaltrust.Middleware.Contracts.Repositories;
using fiskaltrust.Middleware.Queue.Extensions;
using fiskaltrust.Middleware.Queue.Models;
using fiskaltrust.storage.serialization.V0;
using fiskaltrust.storage.V0;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;

namespace fiskaltrust.Middleware.Queue
{
    public class OperationalQueueItem : ftQueueItem
    {
        public string OperationId { get; set; }
    }

    public class SignProcessorV2
    {
        private readonly IMarketSpecificSignProcessor _countrySpecificSignProcessor;
        private readonly ILogger<SignProcessorV2> _logger;
        private readonly IConfigurationRepository _configurationRepository;
        private readonly IMiddlewareQueueItemRepository _queueItemRepository;
        private readonly IReceiptJournalRepository _receiptJournalRepository;
        private readonly IActionJournalRepository _actionJournalRepository;
        private readonly ICryptoHelper _cryptoHelper;
        private readonly Guid _queueId = Guid.Empty;
        private readonly Guid _cashBoxId = Guid.Empty;
        private readonly bool _isSandbox;
        private readonly SignatureFactory _signatureFactory;
        private IManagedMqttClient _mqttClient;

        public Dictionary<string, SignRequestState> Results { get; set; } = new Dictionary<string, SignRequestState>();
        //private readonly Action<string> _onMessage;

        public SignProcessorV2(
            ILogger<SignProcessorV2> logger,
            IConfigurationRepository configurationRepository,
            IMiddlewareQueueItemRepository queueItemRepository,
            IReceiptJournalRepository receiptJournalRepository,
            IActionJournalRepository actionJournalRepository,
            ICryptoHelper cryptoHelper,
            IMarketSpecificSignProcessor countrySpecificSignProcessor,
            MiddlewareConfiguration configuration)
        {
            _logger = logger;
            _configurationRepository = configurationRepository ?? throw new ArgumentNullException(nameof(configurationRepository));
            _countrySpecificSignProcessor = countrySpecificSignProcessor;
            _queueItemRepository = queueItemRepository;
            _receiptJournalRepository = receiptJournalRepository;
            _actionJournalRepository = actionJournalRepository;
            _cryptoHelper = cryptoHelper;
            _queueId = configuration.QueueId;
            _cashBoxId = configuration.CashBoxId;
            _isSandbox = configuration.IsSandbox;
            _signatureFactory = new SignatureFactory();
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

            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                try
                {
                    _logger.LogInformation("Received new message for topic {topic} with responsetopic {responsetopic}", e.ApplicationMessage.Topic, e.ApplicationMessage.ResponseTopic);
                    if (e.ApplicationMessage.Topic == $"{_cashBoxId}/signrequest/state")
                    {
                        _logger.LogInformation("Trying to enqueue {topic}", e.ApplicationMessage.ResponseTopic);
                        var ss = new MqttApplicationMessageBuilder()
                            .WithTopic(e.ApplicationMessage.ResponseTopic)
                            .WithPayload(JsonConvert.SerializeObject(Results.Values.ToList()))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                            .Build();
                        await _mqttClient.EnqueueAsync(ss);
                        await e.AcknowledgeAsync(CancellationToken.None);
                        _logger.LogInformation("Published new message to {topic}", e.ApplicationMessage.ResponseTopic);
                    }
                    else
                    {
                        _logger.LogInformation("Trying tasso enqueue {topic}", e.ApplicationMessage.ResponseTopic);
                        var signRequestMessage = JsonConvert.DeserializeObject<SignRequest>(e.ApplicationMessage.ConvertPayloadToString());
                        if (Results.ContainsKey(signRequestMessage.operationId))
                        {
                            _logger.LogInformation("This should not be possible");
                        }
                        else
                        {
                            var result = await QueueQueueItemAsync(signRequestMessage.request, signRequestMessage.operationId);
                            Results.Add(signRequestMessage.operationId, new SignRequestState(signRequestMessage.operationId, _queueId, result.ftQueueItemId, "Pending", null, null));

                            await e.AcknowledgeAsync(CancellationToken.None);
                            await _mqttClient.EnqueueAsync(e.ApplicationMessage.ResponseTopic, JsonConvert.SerializeObject(new SignRequestAccepted(result.OperationId, result.ftQueueId, result.ftQueueItemId)), MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
                            _logger.LogInformation("Published new message to {topic}", e.ApplicationMessage.ResponseTopic);
                            await ProcessQueueItemAsync(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message for topic  {topic}", e.ApplicationMessage.Topic);
                    throw;
                }
            };
            await _mqttClient.SubscribeAsync($"{_cashBoxId}/signrequest");
            await _mqttClient.SubscribeAsync($"{_cashBoxId}/signrequest/state");
        }

        private async Task<OperationalQueueItem> QueueQueueItemAsync(ReceiptRequest data, string operationId)
        {
            _logger.LogInformation("SignProcessor.ProcessAsync called.");
            try
            {
                if (data == null)
                {
                    throw new ArgumentNullException(nameof(data));
                }
                if (!Guid.TryParse(data.ftCashBoxID, out var dataCashBoxId))
                {
                    throw new InvalidCastException($"Cannot parse CashBoxId {data.ftCashBoxID}");
                }
                if (dataCashBoxId != _cashBoxId)
                {
                    throw new Exception("Provided CashBoxId does not match current CashBoxId");
                }

                var queue = await _configurationRepository.GetQueueAsync(_queueId).ConfigureAwait(false);
                var queueItem = new OperationalQueueItem
                {
                    ftQueueItemId = Guid.NewGuid(),
                    ftQueueId = queue.ftQueueId,
                    ftQueueMoment = DateTime.UtcNow,
                    ftQueueTimeout = queue.Timeout,
                    cbReceiptMoment = data.cbReceiptMoment,
                    cbTerminalID = data.cbTerminalID,
                    cbReceiptReference = data.cbReceiptReference,
                    ftQueueRow = ++queue.ftQueuedRow,
                    OperationId = operationId
                };
                if (queueItem.ftQueueTimeout == 0)
                {
                    queueItem.ftQueueTimeout = 15000;
                }
                queueItem.country = ReceiptRequestHelper.GetCountry(data);
                queueItem.version = ReceiptRequestHelper.GetRequestVersion(data);
                queueItem.request = JsonConvert.SerializeObject(data);
                queueItem.requestHash = _cryptoHelper.GenerateBase64Hash(queueItem.request);
                await _queueItemRepository.InsertOrUpdateAsync(queueItem).ConfigureAwait(false);
                await _configurationRepository.InsertOrUpdateQueueAsync(queue).ConfigureAwait(false);
                return queueItem;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "");
                throw;
            }
        }

        private async Task ProcessQueueItemAsync(OperationalQueueItem queueItem)
        {
            var queue = await _configurationRepository.GetQueueAsync(_queueId).ConfigureAwait(false);
            var data = JsonConvert.DeserializeObject<ReceiptRequest>(queueItem.request);
            try
            {
                Results[queueItem.OperationId] = new SignRequestState(queueItem.OperationId, queueItem.ftQueueId, queueItem.ftQueueItemId, "Processing", null, null);
                queueItem.ftWorkMoment = DateTime.UtcNow;
                _logger.LogTrace("SignProcessor.InternalSign: Calling country specific SignProcessor.");
                (var receiptResponse, var countrySpecificActionJournals) = await _countrySpecificSignProcessor.ProcessAsync(data, queue, queueItem).ConfigureAwait(false);
                _logger.LogTrace("SignProcessor.InternalSign: Country specific SignProcessor finished.");
                new List<ftActionJournal>().AddRange(countrySpecificActionJournals);
                if (_isSandbox)
                {
                    receiptResponse.ftSignatures = receiptResponse.ftSignatures.Concat(_signatureFactory.CreateSandboxSignature(_queueId));
                }
                queueItem.response = JsonConvert.SerializeObject(receiptResponse);
                queueItem.responseHash = _cryptoHelper.GenerateBase64Hash(queueItem.response);
                queueItem.ftDoneMoment = DateTime.UtcNow;
                queue.ftCurrentRow++;

                _logger.LogTrace("SignProcessor.InternalSign: Updating QueueItem in database.");
                await _queueItemRepository.InsertOrUpdateAsync(queueItem).ConfigureAwait(false);
                _logger.LogTrace("SignProcessor.InternalSign: Updating Queue in database.");
                await _configurationRepository.InsertOrUpdateQueueAsync(queue).ConfigureAwait(false);

                if ((receiptResponse.ftState & 0xFFFF_FFFF) == 0xEEEE_EEEE)
                {
                    // TODO: This state indicates that something went wrong while processing the receipt request.
                    //       While we will probably introduce a parameter for this we are right now just returning
                    //       the receipt response as it is.
                    //       Another thing that needs to be considered is if and when we put things into the security
                    //       mechanism. Since there might be cases where we still need to store it though.
                }
                else
                {
                    _logger.LogTrace("SignProcessor.InternalSign: Adding ReceiptJournal to database.");
                    _ = await CreateReceiptJournalAsync(queue, queueItem, data).ConfigureAwait(false);
                }
                Results[queueItem.OperationId] = new SignRequestState(queueItem.OperationId, queueItem.ftQueueId, queueItem.ftQueueItemId, "Done", null, JsonConvert.SerializeObject(receiptResponse));
                await _mqttClient.EnqueueAsync($"{_cashBoxId}/signrequest/{queueItem.OperationId}/done", JsonConvert.SerializeObject(new SignRequestDoneMessage(receiptResponse)), MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
            }
            finally
            {
                foreach (var actionJournal in new List<ftActionJournal>())
                {
                    await _actionJournalRepository.InsertAsync(actionJournal).ConfigureAwait(false);
                }
            }
        }

        public async Task<ftReceiptJournal> CreateReceiptJournalAsync(ftQueue queue, ftQueueItem queueItem, ReceiptRequest receiptrequest)
        {
            queue.ftReceiptNumerator++;
            var receiptjournal = new ftReceiptJournal
            {
                ftReceiptJournalId = Guid.NewGuid(),
                ftQueueId = queue.ftQueueId,
                ftQueueItemId = queueItem.ftQueueItemId,
                ftReceiptMoment = DateTime.UtcNow,
                ftReceiptNumber = queue.ftReceiptNumerator
            };
            if (receiptrequest.cbReceiptAmount.HasValue)
            {
                receiptjournal.ftReceiptTotal = receiptrequest.cbReceiptAmount.Value;
            }
            else
            {
                receiptjournal.ftReceiptTotal = (receiptrequest?.cbChargeItems?.Sum(ci => ci.Amount)).GetValueOrDefault();
            }
            receiptjournal.ftReceiptHash = _cryptoHelper.GenerateBase64ChainHash(queue.ftReceiptHash, receiptjournal, queueItem);
            await _receiptJournalRepository.InsertAsync(receiptjournal).ConfigureAwait(false);
            await UpdateQueuesLastReceipt(queue, receiptjournal).ConfigureAwait(false);

            return receiptjournal;
        }

        private async Task UpdateQueuesLastReceipt(ftQueue queue, ftReceiptJournal receiptJournal)
        {
            queue.ftReceiptHash = receiptJournal.ftReceiptHash;
            queue.ftReceiptTotalizer += receiptJournal.ftReceiptTotal;
            await _configurationRepository.InsertOrUpdateQueueAsync(queue).ConfigureAwait(false);
        }
    }

    public record SignRequest(string operationId, int lifetime, ReceiptRequest request);

    public record SignRequestAccepted(string operationId, Guid queueId, Guid queueItemId);

    public record SignRequestDoneMessage(ReceiptResponse response);

    public record SignRequestState(string operationId, Guid queueId, Guid? queueItemId, string state, string stateMessage, string stateData);
}