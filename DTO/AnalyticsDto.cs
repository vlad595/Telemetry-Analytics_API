using System;

namespace Telemetry_Analytics_API.DTO
{
    public record AggregatedMetricDto(
        DateTime Period, 
        double AvgCpu, 
        double MaxCpu, 
        double AvgMemory, 
        long AvgNetworkRx, 
        long AvgNetworkTx);

    public record SystemStatsDto(
        long TotalMetricsRows, 
        long ActiveAgents);
}