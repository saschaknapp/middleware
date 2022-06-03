﻿using System;
using fiskaltrust.ifPOS.v1;
using fiskaltrust.Middleware.Localization.QueueAT.Extensions;
using fiskaltrust.storage.V0;
using Microsoft.Extensions.DependencyInjection;

namespace fiskaltrust.Middleware.Localization.QueueAT.RequestCommands.Factories
{
    public class RequestCommandFactory : IRequestCommandFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public RequestCommandFactory(IServiceProvider serviceCollection) => _serviceProvider = serviceCollection;

        public RequestCommand Create(ftQueue queue, ftQueueAT queueAT, ReceiptRequest request)
        {
            // Queue is not active, and receipt is not initial-operation
            if ((queue.IsNew() || queue.IsDeactivated()) && !request.IsInitialOperationReceipt())
            {
                return _serviceProvider.GetRequiredService<DisabledQueueReceiptCommand>();
            }
            
            if (request.HasFailedReceiptFlag())
            {
                // The receipt was created in a moment where the POS terminal was not able to communicate with the Middleware.
                // Therefore, a receipt was generated by the POS terminal on its own with the hint "electronic recording system failed" and a failure counter.
                // After the communication between POS terminal and the Middleware is re-established, the receipts are sent with a special ftReceiptCaseFlag to be finally included into the receipt chain.
                return _serviceProvider.GetRequiredService<UsedFailedReceiptCommand>();
            }

            if (request.HasHandwrittenReceiptFlag())
            {
                // The receipt was created in a moment where the POS terminal itself could not be used.
                // Therefore, a handwritten receipt and a copy was created that was then handed out.
                // After recovering the POS terminal, the the receipts are sent with a special ftReceiptCaseFlag to be finally included into the receipt chain.
                // In Germany, there is no requirement to send this receipts to the SCU/TSE.
                return _serviceProvider.GetRequiredService<HandwrittenReceiptCommand>();
            }

            // In failed mode, don't even try to access the SCU
            if (queueAT.SSCDFailCount > 0 && !request.IsZeroReceipt())
            {
                return _serviceProvider.GetRequiredService<SSCDFailedReceiptCommand>();
            }

            return (request.ftReceiptCase & 0xFFFF) switch
            {
                0x0002 => _serviceProvider.GetRequiredService<ZeroReceiptCommand>(),
                0x0003 => _serviceProvider.GetRequiredService<InitialOperationReceiptCommand>(),
                0x0004 => _serviceProvider.GetRequiredService<OutOfOperationReceiptCommand>(),
                0x0005 => _serviceProvider.GetRequiredService<MonthlyClosingReceiptCommand>(),
                0x0006 => _serviceProvider.GetRequiredService<YearlyClosingReceiptCommand>(),
                _ => _serviceProvider.GetRequiredService<NormalReceiptCommand>()
            };
        }
    }
}