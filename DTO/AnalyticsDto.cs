using System;

namespace Telemetry_Analytics_API.DTO
{
    public record AggregatedMetricDto(
        DateTime Period, 
        double AvgCpu, 
        double MaxCpu, 
        double AvgMemory, 
        double AvgNetworkRx, 
        double AvgNetworkTx
    );
    public record SystemStatsDto(
        long TotalMetricsRows, 
        long ActiveAgents
    );

    public enum AggregationType { Avg, Max, Min, Sum, P95, P99 }
    public enum TimeInterval { Minute, FiveMinutes, Hour, Day }

    public class CustomMetricRule
    {
        public string MetricName { get; set; } = string.Empty;
        public AggregationType Aggregation { get; set; }
        public string Alias { get; set; } = string.Empty;
    }

    public class DynamicAnalyticsRequest
    {
        public Guid AgentId { get; set; }
        public int HoursAgo { get; set; }
        public TimeInterval GroupingInterval { get; set; }
        public List<CustomMetricRule> Rules { get; set; } = new();
    }

    public class DailyPeakDto
    {
        public DateTime Period { get; set; }
        public double P95Cpu { get; set; }
        public double P95Memory { get; set; }
    }

    public class NetworkTrendDto
    {
        public DateTime Period { get; set; }
        public double TotalRxMegabytes { get; set; }
        public double TotalTxMegabytes { get; set; }
    }

    public class AnomalyDto
    {
        public DateTime ExactTime { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
    }

    public class DiskConsumptionDto
    {
        public double StartDiskSpace { get; set; }
        public double EndDiskSpace { get; set; }
        public double ConsumedDifference => StartDiskSpace - EndDiskSpace;
    }

    public class HealthScoreDto
    {
        public int TotalMeasurements { get; set; }
        public int OverloadedMeasurements { get; set; }
        public double HealthPercentage => TotalMeasurements == 0 ? 100 : 
            100.0 - ((double)OverloadedMeasurements / TotalMeasurements * 100.0);
    }
}