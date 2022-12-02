﻿using System;

namespace fiskaltrust.Middleware.Storage.Azure.TableEntities.Configuration
{
    public class AzureFtQueueFR : BaseTableEntity
    {
        public double BCITotalReducedS { get; set; }
        public double BCITotalReduced2 { get; set; }
        public double BCITotalReduced1 { get; set; }
        public double BCITotalNormal { get; set; }
        public double BTotalizer { get; set; }
        public long BNumerator { get; set; }
        public Guid? GLastYearQueueItemId { get; set; }
        public DateTime? GLastYearMoment { get; set; }
        public double GYearPITotalUnknown { get; set; }
        public double GYearPITotalInternal { get; set; }
        public double GYearPITotalNonCash { get; set; }
        public double GYearPITotalCash { get; set; }
        public double GYearCITotalUnknown { get; set; }
        public double GYearCITotalZero { get; set; }
        public double GYearCITotalReducedS { get; set; }
        public double GYearCITotalReduced2 { get; set; }
        public double GYearCITotalReduced1 { get; set; }
        public double GYearCITotalNormal { get; set; }
        public double GYearTotalizer { get; set; }
        public Guid? GLastMonthQueueItemId { get; set; }
        public DateTime? GLastMonthMoment { get; set; }
        public double GMonthPITotalUnknown { get; set; }
        public double GMonthPITotalInternal { get; set; }
        public double GMonthPITotalNonCash { get; set; }
        public double GMonthPITotalCash { get; set; }
        public double GMonthCITotalUnknown { get; set; }
        public double GMonthCITotalZero { get; set; }
        public double GMonthCITotalReducedS { get; set; }
        public double GMonthCITotalReduced2 { get; set; }
        public double BCITotalZero { get; set; }
        public double BCITotalUnknown { get; set; }
        public double BPITotalCash { get; set; }
        public double BPITotalNonCash { get; set; }
        public Guid? UsedFailedQueueItemId { get; set; }
        public DateTime? UsedFailedMomentMax { get; set; }
        public DateTime? UsedFailedMomentMin { get; set; }
        public int UsedFailedCount { get; set; }
        public string CLastHash { get; set; }
        public double CTotalizer { get; set; }
        public long CNumerator { get; set; }
        public string XLastHash { get; set; }
        public double XTotalizer { get; set; }
        public long XNumerator { get; set; }
        public Guid? ALastQueueItemId { get; set; }
        public DateTime? ALastMoment { get; set; }
        public double APITotalUnknown { get; set; }
        public double APITotalInternal { get; set; }
        public double GMonthCITotalReduced1 { get; set; }
        public double APITotalNonCash { get; set; }
        public double ACITotalUnknown { get; set; }
        public double ACITotalZero { get; set; }
        public double ACITotalReducedS { get; set; }
        public double ACITotalReduced2 { get; set; }
        public double ACITotalReduced1 { get; set; }
        public double ACITotalNormal { get; set; }
        public double ATotalizer { get; set; }
        public string ALastHash { get; set; }
        public long ANumerator { get; set; }
        public string LLastHash { get; set; }
        public long LNumerator { get; set; }
        public string BLastHash { get; set; }
        public double BPITotalUnknown { get; set; }
        public double BPITotalInternal { get; set; }
        public double APITotalCash { get; set; }
        public double GMonthCITotalNormal { get; set; }
        public double GMonthTotalizer { get; set; }
        public Guid? GLastDayQueueItemId { get; set; }
        public double ICITotalReducedS { get; set; }
        public double ICITotalReduced2 { get; set; }
        public double ICITotalReduced1 { get; set; }
        public double ICITotalNormal { get; set; }
        public double ITotalizer { get; set; }
        public long INumerator { get; set; }
        public string PLastHash { get; set; }
        public double PPITotalUnknown { get; set; }
        public double PPITotalInternal { get; set; }
        public double PPITotalNonCash { get; set; }
        public double PPITotalCash { get; set; }
        public double PTotalizer { get; set; }
        public long PNumerator { get; set; }
        public string TLastHash { get; set; }
        public double ICITotalZero { get; set; }
        public double TPITotalUnknown { get; set; }
        public double TPITotalNonCash { get; set; }
        public double TPITotalCash { get; set; }
        public double TCITotalUnknown { get; set; }
        public double TCITotalZero { get; set; }
        public double TCITotalReducedS { get; set; }
        public double TCITotalReduced2 { get; set; }
        public double TCITotalReduced1 { get; set; }
        public double TCITotalNormal { get; set; }
        public double TTotalizer { get; set; }
        public long TNumerator { get; set; }
        public string CashBoxIdentification { get; set; }
        public string Siret { get; set; }
        public Guid ftSignaturCreationUnitFRId { get; set; }
        public Guid ftQueueFRId { get; set; }
        public double TPITotalInternal { get; set; }
        public int MessageCount { get; set; }
        public double ICITotalUnknown { get; set; }
        public double IPITotalNonCash { get; set; }
        public DateTime? GLastDayMoment { get; set; }
        public double GDayPITotalUnknown { get; set; }
        public double GDayPITotalInternal { get; set; }
        public double GDayPITotalNonCash { get; set; }
        public double GDayPITotalCash { get; set; }
        public double GDayCITotalUnknown { get; set; }
        public double GDayCITotalZero { get; set; }
        public double GDayCITotalReducedS { get; set; }
        public double GDayCITotalReduced2 { get; set; }
        public double GDayCITotalReduced1 { get; set; }
        public double GDayCITotalNormal { get; set; }
        public double GDayTotalizer { get; set; }
        public Guid? GLastShiftQueueItemId { get; set; }
        public DateTime? GLastShiftMoment { get; set; }
        public double IPITotalCash { get; set; }
        public double GShiftPITotalUnknown { get; set; }
        public double GShiftPITotalNonCash { get; set; }
        public double GShiftPITotalCash { get; set; }
        public double GShiftCITotalUnknown { get; set; }
        public double GShiftCITotalZero { get; set; }
        public double GShiftCITotalReducedS { get; set; }
        public double GShiftCITotalReduced2 { get; set; }
        public double GShiftCITotalReduced1 { get; set; }
        public double GShiftCITotalNormal { get; set; }
        public double GShiftTotalizer { get; set; }
        public string GLastHash { get; set; }
        public long GNumerator { get; set; }
        public string ILastHash { get; set; }
        public double IPITotalUnknown { get; set; }
        public double IPITotalInternal { get; set; }
        public double GShiftPITotalInternal { get; set; }
        public DateTime? MessageMoment { get; set; }
        public long TimeStamp { get; set; }
    }
}
