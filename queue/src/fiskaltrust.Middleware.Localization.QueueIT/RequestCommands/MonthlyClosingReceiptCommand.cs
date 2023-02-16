﻿using System;
using System.Threading.Tasks;
using fiskaltrust.ifPOS.v1;
using fiskaltrust.storage.V0;
using fiskaltrust.Middleware.Contracts.RequestCommands;
using fiskaltrust.ifPOS.v1.it;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace fiskaltrust.Middleware.Localization.QueueIT.RequestCommands
{
    internal class MonthlyClosingReceiptCommand : RequestCommandIT
    {
        public MonthlyClosingReceiptCommand(IServiceProvider services)  { }

        public override Task<RequestCommandResponse> ExecuteAsync(IITSSCD client, ftQueue queue, ReceiptRequest request, ftQueueItem queueItem, ftQueueIT queueIt)
        {
            var receiptResponse = CreateReceiptResponse(queue, request, queueItem, CountryBaseState);
            var actionJournalEntry = CreateActionJournal(queue.ftQueueId, request.ftReceiptCase, queueItem.ftQueueItemId, "Monthly-closing receipt was processed.",
                JsonConvert.SerializeObject(new { ftReceiptNumerator = queue.ftReceiptNumerator + 1 }));
            var requestCommandResponse = new RequestCommandResponse
            {
                ReceiptResponse = receiptResponse,
                ActionJournals = new List<ftActionJournal> { actionJournalEntry }
            };
            return Task.FromResult(requestCommandResponse);
        }

    }
}
