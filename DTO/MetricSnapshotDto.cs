namespace Telemetry_Analytics_API.DTO
{
    public sealed class MetricSnapshotDto
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskSpace { get; set; }
        public double NetworkRxBytes { get; set; }
        public double NetworkTxBytes { get; set; }
    }
}
