﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using fiskaltrust.ifPOS.v1;
using fiskaltrust.ifPOS.v1.it;
using fiskaltrust.Middleware.Abstractions;
using fiskaltrust.Middleware.Contracts.Interfaces;
using fiskaltrust.Middleware.Contracts.Models;
using fiskaltrust.Middleware.Localization.QueueIT.Constants;
using fiskaltrust.storage.serialization.DE.V0;
using fiskaltrust.storage.V0;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace fiskaltrust.Middleware.Localization.QueueIT.UnitTest
{
    public class SignProcessorITTests
    {
        private static Guid _queueID = new Guid();

        private readonly ftQueue _queue = new ftQueue
        {
            ftQueueId = _queueID,
        };

        private readonly ftQueue _queueStarted = new ftQueue
        {
            ftQueueId = _queueID,
            StartMoment = DateTime.UtcNow,
            ftReceiptNumerator = 1
        };

        private readonly ftQueue _queueStopped = new ftQueue
        {
            ftQueueId = _queueID,
            StartMoment = DateTime.UtcNow,
            StopMoment = DateTime.UtcNow
        };

        private readonly ftQueueIT _queueIT = new ftQueueIT
        {
            ftQueueITId = _queueID,
            ftSignaturCreationUnitITId = Guid.NewGuid(),
        };

        private readonly ftQueueIT _queueITSCUDeviceOutOfService = new ftQueueIT
        {
            ftQueueITId = _queueID,
            ftSignaturCreationUnitITId = Guid.NewGuid(),
            SSCDFailCount = 1,
            SSCDFailMoment = DateTime.UtcNow,
            SSCDFailQueueItemId = Guid.NewGuid()
        };


        private IMarketSpecificSignProcessor GetSCUDeviceOutOfServiceSUT(ftQueue queue) => GetSUT(queue, _queueIT);

        private IMarketSpecificSignProcessor GetDefaultSUT(ftQueue queue) => GetSUT(queue, _queueIT);

        public static SignaturItem[] CreateFakeReceiptSignatures()
        {
            return new SignaturItem[]
            {
                new SignaturItem
                {
                    Caption = "<receipt-number>",
                    Data = "0002",
                    ftSignatureFormat = (long) SignaturItem.Formats.Text,
                    ftSignatureType = 0x4954000000000012
                },
                new SignaturItem
                {
                    Caption = "<z-number>",
                    Data = "0001",
                    ftSignatureFormat = (long) SignaturItem.Formats.Text,
                    ftSignatureType = 0x4954000000000011
                },
                new SignaturItem
                {
                    Caption = "<receipt-amount>",
                    Data = 23.01.ToString(Cases.CurrencyFormatter),
                    ftSignatureFormat = (long) SignaturItem.Formats.Text,
                    ftSignatureType = 0x4954000000000014
                },
                new SignaturItem
                {
                    Caption = "<receipt-timestamp>",
                    Data = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ftSignatureFormat = (long) SignaturItem.Formats.Text,
                    ftSignatureType = 0x4954000000000013
                }
            };
        }


        private IMarketSpecificSignProcessor GetSUT(ftQueue queue, ftQueueIT queueIT)
        {
            var configurationRepositoryMock = new Mock<IConfigurationRepository>();
            configurationRepositoryMock.Setup(x => x.GetQueueAsync(_queue.ftQueueId)).ReturnsAsync(queue);
            configurationRepositoryMock.Setup(x => x.GetQueueITAsync(_queue.ftQueueId)).ReturnsAsync(queueIT);
            configurationRepositoryMock.Setup(x => x.GetSignaturCreationUnitITAsync(_queueIT.ftSignaturCreationUnitITId.Value)).ReturnsAsync(new ftSignaturCreationUnitIT
            {
                ftSignaturCreationUnitITId = _queueIT.ftSignaturCreationUnitITId.Value,
                InfoJson = null
            });

            var itSSCDMock = new Mock<IITSSCD>();
            itSSCDMock.Setup(x => x.ProcessReceiptAsync(It.IsAny<ProcessRequest>())).ReturnsAsync((ProcessRequest request) =>
            {
                request.ReceiptResponse.ftSignatures = CreateFakeReceiptSignatures();
                return new ProcessResponse
                {
                    ReceiptResponse = request.ReceiptResponse
                };
            });
            itSSCDMock.Setup(x => x.GetRTInfoAsync()).ReturnsAsync(new RTInfo());

            var clientFactoryMock = new Mock<IClientFactory<IITSSCD>>();
            clientFactoryMock.Setup(x => x.CreateClient(It.IsAny<ClientConfiguration>())).Returns(itSSCDMock.Object);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddSingleton(configurationRepositoryMock.Object);
            serviceCollection.AddSingleton(Mock.Of<IJournalITRepository>());
            serviceCollection.AddSingleton(clientFactoryMock.Object);
            serviceCollection.AddSingleton(new MiddlewareConfiguration
            {
                Configuration = new Dictionary<string, object>
                {
                    { "init_ftSignaturCreationUnitIT", "[{\"Url\":\"grpc://localhost:14300\"}]" }
                }
            });

            var bootstrapper = new QueueITBootstrapper();
            bootstrapper.ConfigureServices(serviceCollection);

            return serviceCollection.BuildServiceProvider().GetRequiredService<IMarketSpecificSignProcessor>();
        }

        public static IEnumerable<object[]> allNonInitialOperationReceipts()
        {
            foreach (var number in Enum.GetValues(typeof(ITReceiptCases)))
            {
                if ((long) number == (long) ITReceiptCases.InitialOperationReceipt0x4001)
                {
                    continue;
                }

                yield return new object[] { number };
            }
        }

        public static IEnumerable<object[]> allNonZeroReceiptReceipts()
        {
            foreach (var number in Enum.GetValues(typeof(ITReceiptCases)))
            {
                if ((long) number == (long) ITReceiptCases.ZeroReceipt0x200)
                {
                    continue;
                }

                yield return new object[] { number };
            }
        }

        public static IEnumerable<object[]> allReceipts()
        {
            foreach (var number in Enum.GetValues(typeof(ITReceiptCases)))
            {
                yield return new object[] { number };
            }
        }

        public static IEnumerable<object[]> rtHandledReceipts()
        {
            yield return new object[] { ITReceiptCases.UnknownReceipt0x0000 };
            yield return new object[] { ITReceiptCases.PointOfSaleReceipt0x0001 };
            yield return new object[] { ITReceiptCases.Protocol0x0005 };
            yield return new object[] { ITReceiptCases.InvoiceUnknown0x1000 };
            yield return new object[] { ITReceiptCases.InvoiceB2C0x1001 };
            yield return new object[] { ITReceiptCases.InvoiceB2B0x1002 };
            yield return new object[] { ITReceiptCases.InvoiceB2G0x1003 };
        }

        [Theory]
        [MemberData(nameof(allNonInitialOperationReceipts))]
        public async Task AllNonInitialOperationReceiptCases_ShouldReturnDisabledMessage_IfQueueHasNotStarted(ITReceiptCases receiptCase)
        {
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "{{Guid.NewGuid()}}",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": {{0x4954200000000000 | (long) receiptCase}},
    "ftReceiptCaseData": "",
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queue);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queue, new ftQueueItem { });

            receiptResponse.ftSignatures.Should().BeEmpty();
            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0001);

            actionJournals.Should().HaveCount(1);
            actionJournals[0].Message.Should().Be($"QueueId {_queue.ftQueueId} is not activated yet.");
        }

        [Theory]
        [MemberData(nameof(allReceipts))]
        public async Task AllReceiptCases_ShouldReturnDisabledMessage_IfQueueIsDeactivated(ITReceiptCases receiptCase)
        {
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "{{Guid.NewGuid()}}",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": {{0x4954200000000000 | (long) receiptCase}},
    "ftReceiptCaseData": "",
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queueStopped);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStopped, new ftQueueItem { });

            receiptResponse.ftSignatures.Should().BeEmpty();
            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0001);

            actionJournals.Should().HaveCount(1);
            actionJournals[0].Message.Should().Be($"QueueId {_queue.ftQueueId} has been disabled.");
        }

        [Theory]
        [MemberData(nameof(allNonZeroReceiptReceipts))]
        public async Task AllReceiptCases_ShouldReturnInFailureMode_IfQueueIsInFailedMode(ITReceiptCases receiptCase)
        {
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "{{Guid.NewGuid()}}",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": {{0x4954200000000000 | (long) receiptCase}},
    "ftReceiptCaseData": "",
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetSUT(_queueStarted, _queueITSCUDeviceOutOfService);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStarted, new ftQueueItem { });

            receiptResponse.ftSignatures.Should().BeEmpty();
            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0002);
        }

        [Theory]
        [MemberData(nameof(rtHandledReceipts))]
        public async Task AllReceiptCases_ShouldContain_ZNumber_And_DocumentNumber_InReceiptIdentification(ITReceiptCases receiptCase)
        {
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "{{Guid.NewGuid()}}",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": {{0x4954200000000000 | (long) receiptCase}},
    "ftReceiptCaseData": "",
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queueStarted);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStarted, new ftQueueItem { });

            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0000);
            receiptResponse.ftReceiptIdentification.Should().Be("ft1#0001-0002");
        }

        [Fact]
        public async Task Process_InitialOperationReceipt()
        {
            var initOperationReceipt = $$"""
{
    "ftCashBoxId": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "INIT",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": 5283883447184539649,
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queue);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queue, new ftQueueItem { });

            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0000);
            actionJournals.Should().HaveCount(1);
            var notification = JsonConvert.DeserializeObject<ActivateQueueSCU>(actionJournals[0].DataJson);
            notification.IsStartReceipt.Should().BeTrue();

            receiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == 0x4954_2000_0001_1000);
        }

        [Fact]
        public async Task Process_OutOfOperationReceipt()
        {
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "OutOfOperation",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": 5283883447184539650,
    "ftReceiptCaseData": "",
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queueStarted);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStarted, new ftQueueItem { });

            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0000);
            actionJournals.Should().HaveCount(1);
            var notification = JsonConvert.DeserializeObject<DeactivateQueueSCU>(actionJournals[0].DataJson);
            notification.IsStopReceipt.Should().BeTrue();
        }

        [Fact]
        public async Task Process_ZeroReceipt()
        {
            var zeroReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "Zero",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": 5283883447184531456,
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(zeroReceipt);
            var sut = GetDefaultSUT(_queueStarted);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStarted, new ftQueueItem { });
            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0000);
            actionJournals.Should().HaveCount(0);
        }

        [Fact]
        public async Task Process_DailyClosingReceipt()
        {
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "Daily-Closing",
    "cbReceiptMoment": "{{DateTime.UtcNow.ToString("o")}}",
    "cbChargeItems": [],
    "cbPayItems": [],
    "ftReceiptCase": 5283883447184531473,
    "cbUser": "Admin"
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queueStarted);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStarted, new ftQueueItem { });

            receiptResponse.ftReceiptIdentification.Should().Be("ft1#Z1");
            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0000);
            actionJournals.Should().HaveCount(1);
            actionJournals[0].Type.Should().Be(receiptRequest.ftReceiptCase.ToString());
        }

        [Fact]
        public async Task ProcessPosReceipt_0x4954_2000_0000_0001()
        {
            var current_moment = DateTime.UtcNow.ToString("o");
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "0001-0002",
    "cbUser": "user1234",
    "cbReceiptMoment": "{{current_moment}}",
    "cbChargeItems": [
        {
            "Quantity": 2.0,
            "Amount": 221,
            "UnitPrice": 110.5,
            "VATRate": 22,
            "Description": "TakeAway - Delivery - Item VAT 22%",
            "ftChargeItemCase": 5283883447186620435,
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 107,
            "VATRate": 10,
            "ftChargeItemCase": 5283883447186620433,
            "Description": "TakeAway - Delivery - Item VAT 10%",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 88,
            "VATRate": 5,
            "ftChargeItemCase": 5283883447186620434,
            "Description": "TakeAway - Delivery - Item VAT 5%",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 90,
            "VATRate": 4,
            "ftChargeItemCase": 5283883447186620436,
            "Description": "TakeAway - Delivery - Item VAT 4%",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186624532,
            "Description": "TakeAway - Delivery - Item VAT NI",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186628628,
            "Description": "TakeAway - Delivery - Item VAT NS",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186632724,
            "Description": "TakeAway - Delivery - Item VAT ES",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186636820,
            "Description": "TakeAway - Delivery - Item VAT RM",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186640916,
            "Description": "TakeAway - Delivery - Item VAT AL",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": 1,
            "Amount": 10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186653204,
            "Description": "TakeAway - Delivery - Item VAT EE",
            "Moment": "{{current_moment}}"
        }
    ],
    "cbPayItems": [
        {
            "Quantity": 1,
            "Description": "Cash",
            "ftPayItemCase": 5283883447184523265,
            "Moment": "{{current_moment}}",
            "Amount": 566
        }
    ],
    "ftReceiptCase": 5283883447184523265
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queueStarted);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStarted, new ftQueueItem { });
            receiptResponse.ftSignatures.Should().HaveCountGreaterOrEqualTo(1);
            receiptResponse.ftReceiptIdentification.Should().Be("ft1#0001-0002");
            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0000);
            actionJournals.Should().HaveCount(0);
        }

        [Fact]
        public async Task ProcessPosReceiptRefund_0x4954_2000_0002_0001()
        {
            var current_moment = DateTime.UtcNow.ToString("o");
            var initOperationReceipt = $$"""
{
    "ftCashBoxID": "00000000-0000-0000-0000-000000000000",
    "ftPosSystemId": "00000000-0000-0000-0000-000000000000",
    "cbTerminalID": "00010001",
    "cbReceiptReference": "0001-0005",
    "cbUser": "user1234",
    "cbReceiptMoment": "{{current_moment}}",
    "cbChargeItems": [
        {
            "Quantity": -2.0,
            "Amount": -221,
            "UnitPrice": 110.5,
            "VATRate": 22,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 22%",
            "ftChargeItemCase": 5283883447186751507,
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -107,
            "VATRate": 10,
            "ftChargeItemCase": 5283883447186751505,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 10%",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -88,
            "VATRate": 5,
            "ftChargeItemCase": 5283883447186751506,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 5%",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -90,
            "VATRate": 4,
            "ftChargeItemCase": 5283883447186751508,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 4%",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186755604,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT NI",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186759700,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT NS",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186763796,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT ES",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186767892,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT RM",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186771988,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT AL",
            "Moment": "{{current_moment}}"
        },
        {
            "Quantity": -1,
            "Amount": -10,
            "VATRate": 0,
            "ftChargeItemCase": 5283883447186784276,
            "Description": "Return/Refund - TakeAway - Delivery - Item VAT EE",
            "Moment": "{{current_moment}}"
        }
    ],
    "cbPayItems": [
        {
            "Quantity": 1,
            "Description": "Return/Refund Cash",
            "ftPayItemCase": 5283883447184654337,
            "Moment": "{{current_moment}}",
            "Amount": -566
        }
    ],
    "ftReceiptCase": 5283883447184654337 
}
""";
            var receiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(initOperationReceipt);
            var sut = GetDefaultSUT(_queueStarted);
            var (receiptResponse, actionJournals) = await sut.ProcessAsync(receiptRequest, _queueStarted, new ftQueueItem { });
            receiptResponse.ftSignatures.Should().HaveCountGreaterOrEqualTo(1);
            receiptResponse.ftState.Should().Be(0x4954_0000_0000_0000);
            receiptResponse.ftReceiptIdentification.Should().Be("ft1#0001-0002");
            actionJournals.Should().HaveCount(0);
        }
    }
}
