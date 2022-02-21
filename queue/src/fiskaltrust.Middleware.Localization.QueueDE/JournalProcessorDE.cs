﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using fiskaltrust.Exports.DSFinVK;
using fiskaltrust.Exports.DSFinVK.Models;
using fiskaltrust.Exports.TAR;
using fiskaltrust.Exports.TAR.Services;
using fiskaltrust.ifPOS.v1;
using fiskaltrust.ifPOS.v1.de;
using fiskaltrust.Middleware.Contracts;
using fiskaltrust.Middleware.Contracts.Constants;
using fiskaltrust.Middleware.Contracts.Models;
using fiskaltrust.Middleware.Contracts.Repositories;
using fiskaltrust.Middleware.Localization.QueueDE.Helpers;
using fiskaltrust.Middleware.Localization.QueueDE.MasterData;
using fiskaltrust.Middleware.Localization.QueueDE.Repositories;
using fiskaltrust.Middleware.Localization.QueueDE.Services;
using fiskaltrust.storage.V0;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace fiskaltrust.Middleware.Localization.QueueDE
{
    public class JournalProcessorDE : IJournalProcessor
    {
        private const string STORE_TEMPORARY_FILES_KEY = "StoreTemporaryExportFiles";

        private readonly ILogger<JournalProcessorDE> _logger;
        private readonly IReadOnlyConfigurationRepository _configurationRepository;
        private readonly IReadOnlyQueueItemRepository _queueItemRepository;
        private readonly IReadOnlyReceiptJournalRepository _receiptJournalRepository;
        private readonly IReadOnlyJournalDERepository _journalDERepository;
        private readonly IMiddlewareRepository<ftReceiptJournal> _middlewareReceiptJournalRepository;
        private readonly IMiddlewareRepository<ftJournalDE> _middlewareJournalDERepository;
        private readonly IReadOnlyActionJournalRepository _actionJournalRepository;
        private readonly IDESSCDProvider _deSSCDProvider;
        private readonly MiddlewareConfiguration _configuration;
        private readonly IMasterDataService _masterDataService;
        private readonly IMiddlewareQueueItemRepository _middlewareQueueItemRepository;

        private readonly bool _storeTemporaryExportFiles = false;

        public JournalProcessorDE(
            ILogger<JournalProcessorDE> logger,
            IReadOnlyConfigurationRepository configurationRepository,
            IReadOnlyQueueItemRepository queueItemRepository,
            IReadOnlyReceiptJournalRepository receiptJournalRepository,
            IReadOnlyJournalDERepository journalDERepository,
            IMiddlewareRepository<ftReceiptJournal> middlewareReceiptJournalRepository,
            IMiddlewareRepository<ftJournalDE> middlewareJournalDERepository,
            IReadOnlyActionJournalRepository actionJournalRepository,
            IDESSCDProvider deSSCDProvider,
            MiddlewareConfiguration configuration,
            IMasterDataService masterDataService,
            IMiddlewareQueueItemRepository middlewareQueueItemRepository)
        {
            _logger = logger;
            _configurationRepository = configurationRepository;
            _queueItemRepository = queueItemRepository;
            _receiptJournalRepository = receiptJournalRepository;
            _journalDERepository = journalDERepository;
            _middlewareReceiptJournalRepository = middlewareReceiptJournalRepository;
            _middlewareJournalDERepository = middlewareJournalDERepository;
            _actionJournalRepository = actionJournalRepository;
            _deSSCDProvider = deSSCDProvider;
            _configuration = configuration;
            _masterDataService = masterDataService;
            _middlewareQueueItemRepository = middlewareQueueItemRepository;

            if (_configuration.Configuration.ContainsKey(STORE_TEMPORARY_FILES_KEY))
            {
                _storeTemporaryExportFiles = bool.TryParse(_configuration.Configuration[STORE_TEMPORARY_FILES_KEY].ToString(), out var val) && val;
            }
        }

        public async IAsyncEnumerable<JournalResponse> ProcessAsync(JournalRequest request)
        {
            _logger.LogDebug($"Processing JournalRequest for DE (Type: {request.ftJournalType:X}");
            if (request.ftJournalType == (long) JournalTypes.TarExportFromTSE)
            {
                await foreach (var value in ProcessTarExportFromTSEAsync(request).ConfigureAwait(false))
                {
                    yield return value;
                }
            }
            else if (request.ftJournalType == (long) JournalTypes.DSFinVKExport)
            {
                await foreach (var value in ProcessDSFinVKExportAsync(request).ConfigureAwait(false))
                {
                    yield return value;
                }
            }
            else if (request.ftJournalType == (long) JournalTypes.TarExportFromDatabase)
            {
                await foreach (var value in ProcessTarExportFromDatabaseAsync(request).ConfigureAwait(false))
                {
                    yield return value;
                }
            }
            else
            {
                var result = new
                {
                    QueueDEList = _configurationRepository.GetQueueDEListAsync().Result
                };
                yield return new JournalResponse
                {
                    Chunk = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result)).ToList()
                };
            }
        }

        private async IAsyncEnumerable<JournalResponse> ProcessTarExportFromDatabaseAsync(JournalRequest request)
        {
            var journalDERepository = new JournalDERepositoryRangeDecorator(_middlewareJournalDERepository, _journalDERepository, request.From, request.To);
            var archiveRepository = new ArchiveFactory();

            var workingDirectory = Path.Combine(_configuration.ServiceFolder, "Exports", _configuration.QueueId.ToString(), "TAR", DateTime.Now.ToString("yyyyMMddhhmmssfff"));
            Directory.CreateDirectory(workingDirectory);

            try
            {
                var exporter = new TarExporter(_logger, journalDERepository, archiveRepository);

                var tarPath = Path.Combine(workingDirectory, "export.tar");

                using (var tarFileStream = await exporter.ExportAsync().ConfigureAwait(false))
                {
                    if (tarFileStream == null || tarFileStream.Length == 0)
                    {
                        _logger.LogWarning("No TAR export was generated.");
                        yield break;
                    }

                    using (var file = File.Open(tarPath, FileMode.Create))
                    {
                        tarFileStream.CopyTo(file);
                    }
                }

                foreach (var chunk in FileHelpers.ReadFileAsChunks(tarPath, request.MaxChunkSize))
                {
                    yield return new JournalResponse
                    {
                        Chunk = chunk.ToList()
                    };
                }
            }
            finally
            {
                if (!_storeTemporaryExportFiles && Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, true);
                }
            }
        }

        private async IAsyncEnumerable<JournalResponse> ProcessTarExportFromTSEAsync(JournalRequest request)
        {
            var exportSession = await _deSSCDProvider.Instance.StartExportSessionAsync(new StartExportSessionRequest()).ConfigureAwait(false);
            var sha256CheckSum = "";
            using (var memoryStream = new MemoryStream())
            {
                ExportDataResponse export;
                do
                {
                    export = await _deSSCDProvider.Instance.ExportDataAsync(new ExportDataRequest
                    {
                        TokenId = exportSession.TokenId,
                        MaxChunkSize = request.MaxChunkSize
                    }).ConfigureAwait(false);
                    if (!export.TotalTarFileSizeAvailable)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                    }
                    else
                    {
                        var chunk = Convert.FromBase64String(export.TarFileByteChunkBase64);
                        memoryStream.Write(chunk, 0, chunk.Length);
                        yield return new JournalResponse
                        {
                            Chunk = chunk.ToList()
                        };
                    }
                } while (!export.TarFileEndOfFile);
                sha256CheckSum = Convert.ToBase64String(SHA256.Create().ComputeHash(memoryStream.ToArray()));
            }

            var endSessionRequest = new EndExportSessionRequest
            {
                TokenId = exportSession.TokenId,
                Sha256ChecksumBase64 = sha256CheckSum
            };
            var endExportSessionResult = await _deSSCDProvider.Instance.EndExportSessionAsync(endSessionRequest).ConfigureAwait(false);
            if (!endExportSessionResult.IsValid)
            {
                throw new Exception("The TAR file export was not successful.");
            }
            yield break;
        }

        private async IAsyncEnumerable<JournalResponse> ProcessDSFinVKExportAsync(JournalRequest request)
        {

            var receiptJournalRepository = new ReceiptJournalRepositoryRangeDecorator(_middlewareReceiptJournalRepository, _receiptJournalRepository, request.From, request.To);

            var queueDE = await _configurationRepository.GetQueueDEAsync(_configuration.QueueId).ConfigureAwait(false);

            var workingDirectory = Path.Combine(_configuration.ServiceFolder, "Exports", queueDE.ftQueueDEId.ToString(), "DSFinV-K", DateTime.Now.ToString("yyyyMMddhhmmssfff"));
            Directory.CreateDirectory(workingDirectory);

            try
            {
                var certificateBase64 = await GetCertificateBase64(queueDE).ConfigureAwait(false);
                var firstZNumber = await GetFirstZNumber(_actionJournalRepository, receiptJournalRepository, request).ConfigureAwait(false);

                var targetDirectory = $"{Path.Combine(workingDirectory, "raw")}{Path.DirectorySeparatorChar}";
                var parameters = new DSFinVKParameters
                {
                    CashboxIdentification = queueDE.CashBoxIdentification,
                    FirstZNumber = firstZNumber,
                    TargetDirectory = targetDirectory,
                    TSECertificateBase64 = certificateBase64
                };

                var readOnlyReceiptReferenceRepository = new ReadOnlyReceiptReferenceRepository(_middlewareQueueItemRepository, _actionJournalRepository);
                var fallbackMasterDataRepo = new ReadOnlyMasterDataConfigurationRepository(_masterDataService.GetFromConfig());

                // No need to wrap the QueueItemRepository, as the DSFinV-K exporter only uses the GetAsync(Guid id) method
                var exporter = new DSFinVKExporter(_logger, receiptJournalRepository, _queueItemRepository, readOnlyReceiptReferenceRepository, fallbackMasterDataRepo);

                await exporter.ExportAsync(parameters).ConfigureAwait(false);

                if (!Directory.Exists(targetDirectory))
                {
                    _logger.LogWarning("No DSFinV-K was generated. Make sure you included the daily-closing receipt in the requested time range.");
                    yield break;
                }

                var zipPath = Path.Combine(workingDirectory, "export.zip");
                await Task.Run(() => ZipFile.CreateFromDirectory(targetDirectory, zipPath)).ConfigureAwait(false);
                foreach (var chunk in FileHelpers.ReadFileAsChunks(zipPath, request.MaxChunkSize))
                {
                    yield return new JournalResponse
                    {
                        Chunk = chunk
                    };
                }
            }
            finally
            {
                if (!_storeTemporaryExportFiles && Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, true);
                }
            }
        }

        private async Task<string> GetCertificateBase64(ftQueueDE queueDE)
        {
            if (queueDE.ftSignaturCreationUnitDEId.HasValue)
            {
                var scuDE = await _configurationRepository.GetSignaturCreationUnitDEAsync(queueDE.ftSignaturCreationUnitDEId.Value).ConfigureAwait(false);
                if (scuDE.TseInfoJson != null)
                {
                    var tseInfo = JsonConvert.DeserializeObject<TseInfo>(scuDE.TseInfoJson);
                    return tseInfo.CertificatesBase64.FirstOrDefault() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private async Task<int> GetFirstZNumber(IReadOnlyActionJournalRepository actionJournalRepository, IReadOnlyReceiptJournalRepository receiptJournalRepository, JournalRequest request)
        {
            var firstZNumber = 1;
            var actionJournals = (await actionJournalRepository.GetAsync().ConfigureAwait(false)).OrderBy(x => x.TimeStamp);
            var receiptJournals = (await receiptJournalRepository.GetAsync().ConfigureAwait(false)).ToList();

            foreach (var actionJournal in actionJournals.Where(x => x.Type == "4445000000000007" || x.Type == "0x4445000000000007"))
            {
                var receiptJournal = receiptJournals.FirstOrDefault(x => x.ftQueueItemId == actionJournal.ftQueueItemId);
                if (receiptJournal != null)
                {
                    if (receiptJournal.TimeStamp >= request.From)
                    {
                        break;
                    }
                }

                firstZNumber++;
            }

            return firstZNumber;
        }
    }
}
