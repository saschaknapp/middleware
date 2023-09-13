﻿using fiskaltrust.ifPOS.v1;
using fiskaltrust.ifPOS.v1.it;
using fiskaltrust.Middleware.Abstractions;
using fiskaltrust.Middleware.SCU.IT.Abstraction;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace fiskaltrust.Middleware.SCU.IT.AcceptanceTests
{
    public abstract class ITSSCDTests
    {
        private static readonly Guid _queueId = Guid.NewGuid();

        private static readonly ReceiptResponse _receiptResponse = new ReceiptResponse
        {
            ftCashBoxIdentification = "00010001",
            ftQueueID = _queueId.ToString()
        };

        protected abstract IMiddlewareBootstrapper GetMiddlewareBootstrapper();

        private IITSSCD GetSUT()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();

            var sut = GetMiddlewareBootstrapper();
            sut.ConfigureServices(serviceCollection);
            return serviceCollection.BuildServiceProvider().GetRequiredService<IITSSCD>();
        }

        [Fact]
        public async Task GetRTInfoAsync_ShouldReturn_Serialnumber()
        {
            var itsscd = GetSUT();

            var result = await itsscd.GetRTInfoAsync();
            result.SerialNumber.Should().Be("96SRT001239");
        }

        [Fact]
        public async Task ProcessPosReceipt_InitialOperation_0x4954_2000_0000_4001()
        {
            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetInitialOperation(),
                ReceiptResponse = _receiptResponse
            });
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.Caption == "<customrtserver-cashuuid>");
        }

        [Fact]
        public async Task ProcessPosReceipt_OutOfOperation_0x4954_2000_0000_4002()
        {
            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetOutOOperation(),
                ReceiptResponse = _receiptResponse
            });
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.Caption == "<customrtserver-cashuuid>");
        }

        [Fact]
        public async Task ProcessPosReceipt_Daily_Closing0x4954_2000_0000_2011()
        {

            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetDailyClosing(),
                ReceiptResponse = _receiptResponse
            });
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == 0x4954000000000011);
        }

        [Fact]
        public async Task ProcessPosReceipt_ZeroReceipt0x4954_2000_0000_2000()
        {
            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetZeroReceipt(),
                ReceiptResponse = _receiptResponse
            });
            var dictioanry = JsonConvert.DeserializeObject<Dictionary<string, object>>(result.ReceiptResponse.ftStateData);
            dictioanry.Should().ContainKey("DeviceMemStatus");
            dictioanry.Should().ContainKey("DeviceDailyStatus");
        }

        [Fact]
        public async Task ProcessPosReceipt_0x4954_2000_0000_0001_TakeAway_Delivery_Cash()
        {
            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetTakeAway_Delivery_Cash(),
                ReceiptResponse = _receiptResponse
            });


            using var scope = new AssertionScope();
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTSerialNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentType));
        }

        [Fact]
        public async Task ProcessPosReceipt_0x4954_2000_0000_0001_TakeAway_Delivery_Cash_Refund()
        {
            var response = _receiptResponse;
            var itsscd = GetSUT();
            var request = ReceiptExamples.GetTakeAway_Delivery_Cash();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = request,
                ReceiptResponse = _receiptResponse
            });


            var zNumber = result.ReceiptResponse.GetSignaturItem(SignatureTypesIT.RTZNumber)?.Data;
            var rtdocNumber = result.ReceiptResponse.GetSignaturItem(SignatureTypesIT.RTDocumentNumber)?.Data;
            var rtDocumentMoment = DateTime.Parse(result.ReceiptResponse.GetSignaturItem(SignatureTypesIT.RTDocumentMoment)?.Data ?? DateTime.MinValue.ToString());
            var signatures = new List<SignaturItem>();
            signatures.AddRange(response.ftSignatures);
            signatures.AddRange(new List<SignaturItem>
                    {
                        new SignaturItem
                        {
                            Caption = "<reference-z-number>",
                            Data = zNumber?.ToString(),
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceZNumber
                        },
                        new SignaturItem
                        {
                            Caption = "<reference-doc-number>",
                            Data = rtdocNumber?.ToString(),
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceDocumentNumber
                        },
                        new SignaturItem
                        {
                            Caption = "<reference-timestamp>",
                            Data = rtDocumentMoment.ToString("yyyy-MM-dd HH:mm:ss"),
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment
                        },
                    });
            response.ftSignatures = signatures.ToArray();

            var refundResult = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetTakeAway_Delivery_Refund(),
                ReceiptResponse = response
            });
            using var scope = new AssertionScope();
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTSerialNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentType));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceDocumentMoment));
        }

        [Fact]
        public async Task ProcessPosReceipt_0x4954_2000_0000_0001_TakeAway_Delivery_Cash_Void()
        {
            var response = _receiptResponse;
            var itsscd = GetSUT();
            var request = ReceiptExamples.GetTakeAway_Delivery_Cash();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = request,
                ReceiptResponse = _receiptResponse
            });


            var zNumber = result.ReceiptResponse.GetSignaturItem(SignatureTypesIT.RTZNumber)?.Data;
            var rtdocNumber = result.ReceiptResponse.GetSignaturItem(SignatureTypesIT.RTDocumentNumber)?.Data;
            var rtDocumentMoment = DateTime.Parse(result.ReceiptResponse.GetSignaturItem(SignatureTypesIT.RTDocumentMoment)?.Data ?? DateTime.MinValue.ToString());
            var signatures = new List<SignaturItem>();
            signatures.AddRange(response.ftSignatures);
            signatures.AddRange(new List<SignaturItem>
                    {
                        new SignaturItem
                        {
                            Caption = "<reference-z-number>",
                            Data = zNumber?.ToString(),
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceZNumber
                        },
                        new SignaturItem
                        {
                            Caption = "<reference-doc-number>",
                            Data = rtdocNumber?.ToString(),
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceDocumentNumber
                        },
                        new SignaturItem
                        {
                            Caption = "<reference-timestamp>",
                            Data = rtDocumentMoment.ToString("yyyy-MM-dd HH:mm:ss"),
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment
                        },
                    });
            response.ftSignatures = signatures.ToArray();

            var refundResult = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetTakeAway_Delivery_Void(),
                ReceiptResponse = response
            });
            using var scope = new AssertionScope();
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTSerialNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentType));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTReferenceDocumentMoment));
        }

        [Fact]
        public async Task ProcessPosReceipt_0x4954_2000_0000_0001_TakeAway_Delivery_Card()
        {
            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetTakeAway_Delivery_Card(),
                ReceiptResponse = _receiptResponse
            });
            using var scope = new AssertionScope();
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTSerialNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentType));
        }

        [Fact]
        public async Task ProcessPosReceipt_0x4954_2000_0000_0001_TakeAway_Delivery_Card_WithCustomerIVa()
        {
            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.GetTakeAway_Delivery_Card_WithCustomerIva(),
                ReceiptResponse = _receiptResponse
            });

            using var scope = new AssertionScope();
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTSerialNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentType));
        }

        [Fact]
        public async Task ProcessPosReceipt_0x4954_2000_0000_0001_CashAndVoucher()
        {
            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = ReceiptExamples.FoodBeverage_CashAndVoucher(),
                ReceiptResponse = _receiptResponse
            });

            using var scope = new AssertionScope();
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTSerialNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentMoment));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (ITConstants.BASE_STATE | (long) SignatureTypesIT.RTDocumentType));
        }

        [Fact]
        public async Task Cancellation()
        {


            var receipt = $$"""
                                {
                    "ftCashBoxID": "47e4621d-a09e-42a9-a424-5c417293e32a",
                    "ftPosSystemId": "c7aa9d1b-fed2-4f5b-8d03-0be252b2541b",
                    "cbTerminalID": "00010001",
                    "cbReceiptReference": "4b388b6d-c5c5-427a-981d-850f22467288",
                    "cbPreviousReceiptReference": "0952f2f9-0f9a-4ec7-ad3b-c71860a4ad9f",
                    "cbUser": "user1234",
                    "cbReceiptMoment": "2023-09-07T07:26:24.215Z",
                    "cbChargeItems": [
                        {
                            "Quantity": -2.0,
                            "Amount": -221,
                            "UnitPrice": 110.5,
                            "VATRate": 22,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 22%",
                            "ftChargeItemCase": 5283883447186751507,
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -107,
                            "VATRate": 10,
                            "ftChargeItemCase": 5283883447186751505,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 10%",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -88,
                            "VATRate": 5,
                            "ftChargeItemCase": 5283883447186751506,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 5%",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -90,
                            "VATRate": 4,
                            "ftChargeItemCase": 5283883447186751508,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT 4%",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -10,
                            "VATRate": 0,
                            "ftChargeItemCase": 5283883447186755604,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT NI",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -10,
                            "VATRate": 0,
                            "ftChargeItemCase": 5283883447186759700,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT NS",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -10,
                            "VATRate": 0,
                            "ftChargeItemCase": 5283883447186763796,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT ES",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -10,
                            "VATRate": 0,
                            "ftChargeItemCase": 5283883447186767892,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT RM",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -10,
                            "VATRate": 0,
                            "ftChargeItemCase": 5283883447186771988,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT AL",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        },
                        {
                            "Quantity": -1,
                            "Amount": -10,
                            "VATRate": 0,
                            "ftChargeItemCase": 5283883447186784276,
                            "Description": "Return/Refund - TakeAway - Delivery - Item VAT EE",
                            "Moment": "2023-09-07T07:26:24.215Z"
                        }
                    ],
                    "cbPayItems": [
                        {
                            "Quantity": 1,
                            "Description": "Return/Refund Cash",
                            "ftPayItemCase": 5283883447184654337,
                            "Moment": "2023-09-07T07:26:24.215Z",
                            "Amount": -566
                        }
                    ],
                    "ftReceiptCase": 5283883447201300481
                }
                """;

            var response = new ReceiptResponse();
            var signatures = new List<SignaturItem>();
            signatures.AddRange(response.ftSignatures);
            signatures.AddRange(new List<SignaturItem>
                    {
                        new SignaturItem
                        {
                            Caption = "<reference-z-number>",
                            Data = "24",
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTReferenceZNumber
                        },
                        new SignaturItem
                        {
                            Caption = "<reference-doc-number>",
                            Data = "47",
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTReferenceDocumentNumber
                        },
                        new SignaturItem
                        {
                            Caption = "<reference-timestamp>",
                            Data = "2023-09-07 09:40",
                            ftSignatureFormat = (long) SignaturItem.Formats.Text,
                            ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTDocumentMoment
                        },
                    });
            response.ftSignatures = signatures.ToArray();

            var itsscd = GetSUT();
            var result = await itsscd.ProcessReceiptAsync(new ProcessRequest
            {
                ReceiptRequest = JsonConvert.DeserializeObject<ReceiptRequest>(receipt),
                ReceiptResponse = response
            });

            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (0x4954000000000000 | (long) SignatureTypesIT.RTZNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (0x4954000000000000 | (long) SignatureTypesIT.RTDocumentNumber));
            result.ReceiptResponse.ftSignatures.Should().Contain(x => x.ftSignatureType == (0x4954000000000000 | (long) SignatureTypesIT.RTSerialNumber));
        }
    }
}

// TODO TEsts
// - Add test that sends wrong CUstomer IVA
// - Add test that sends customer data without iva