﻿using System;
using System.Collections.Generic;
using System.Globalization;
using fiskaltrust.ifPOS.v1;
using fiskaltrust.ifPOS.v1.it;

namespace fiskaltrust.Middleware.SCU.IT.Abstraction;

public static class SignatureFactory
{
    public static SignaturItem[] CreateInitialOperationSignatures()
    {
        return new SignaturItem[] { };
    }
  
    public static SignaturItem[] CreateOutOfOperationSignatures()
    {
        return new SignaturItem[] { };
    }

    public static SignaturItem[] CreateZeroReceiptSignatures()
    {
        return new SignaturItem[] { };
    }

    public static SignaturItem[] CreateDailyClosingReceiptSignatures(long zRepNumber)
    {
        return new SignaturItem[]
        {
            new SignaturItem
            {
                Caption = "<z-number>",
                Data = zRepNumber.ToString(),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTZNumber
            },
        };
    }

    public static SignaturItem[] CreatePosReceiptSignatures(long receiptNumber, long zRepNumber, decimal amount, DateTime receiptDateTime)
    {
        return new SignaturItem[]
        {
            new SignaturItem
            {
                Caption = "<receipt-number>",
                Data = receiptNumber.ToString(),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTDocumentNumber
            },
            new SignaturItem
            {
                Caption = "<z-number>",
                Data = zRepNumber.ToString(),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTZNumber
            },
            new SignaturItem
            {
                Caption = "<receipt-amount>",
                Data = amount.ToString(ITConstants.CurrencyFormatter),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTAmount
            },
            new SignaturItem
            {
                Caption = "<receipt-timestamp>",
                Data = receiptDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTDocumentMoment
            }
        };
    }

    public static SignaturItem[] CreatePosReceiptSignatures(long receiptNumber, long zRepNumber, DateTime receiptDateTime)
    {
        return new SignaturItem[]
        {
            new SignaturItem
            {
                Caption = "<receipt-number>",
                Data = receiptNumber.ToString(),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 |(long) SignatureTypesIT.RTDocumentNumber
            },
            new SignaturItem
            {
                Caption = "<z-number>",
                Data = zRepNumber.ToString(),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 | (long) SignatureTypesIT.RTZNumber
            },
            new SignaturItem
            {
                Caption = "<receipt-timestamp>",
                Data = receiptDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ftSignatureFormat = (long) SignaturItem.Formats.Text,
                ftSignatureType = 0x4954000000000000 |(long) SignatureTypesIT.RTZNumber
            }
        };
    }

    public static SignaturItem[] CreateVoucherSignatures(NonFiscalRequest nonFiscalRequest)
    {

        var signs = new List<SignaturItem>();
        var cnt = nonFiscalRequest.NonFiscalPrints.Count;
        for (var i = 0; i < cnt; i++)
        {
            var dat = nonFiscalRequest.NonFiscalPrints[i].Data;
            if (dat == "***Voucher***")
            {
                var dat2 = i + 1 < cnt ? nonFiscalRequest.NonFiscalPrints[i + 1].Data : null;
                var isAmount = decimal.TryParse(dat2, NumberStyles.Number, new CultureInfo("it-It", false), out var amnt);
                if (!isAmount)
                {
                    dat2 = i + 2 < cnt ? nonFiscalRequest.NonFiscalPrints[i + 2].Data : null;
                    isAmount = decimal.TryParse(dat2, NumberStyles.Number, new CultureInfo("it-It", false), out amnt);
                }
                if (isAmount)
                {
                    signs.Add(new SignaturItem
                    {
                        Caption = "<voucher>",
                        Data = Math.Abs(amnt).ToString(new NumberFormatInfo
                        {
                            NumberDecimalSeparator = ",",
                            NumberGroupSeparator = "",
                            CurrencyDecimalDigits = 2
                        }),
                        ftSignatureFormat = (long) SignaturItem.Formats.Text,
                        ftSignatureType = 0x4954000000000000 & (long) SignatureTypesIT.RTAmount
                    });
                }
            }
        }
        return signs.ToArray();
    }
}