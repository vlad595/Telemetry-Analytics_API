using Microsoft.AspNetCore.Mvc;
using Telemetry_Analytics_API.DTO;
using Telemetry_Analytics_API.Services;

namespace Telemetry_Analytics_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsService _analyticsService;
        private readonly IAnalyticsAccessService _accessService;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(
            IAnalyticsService analyticsService,
            IAnalyticsAccessService accessService,
            ILogger<AnalyticsController> logger)
        {
            _analyticsService = analyticsService;
            _accessService = accessService;
            _logger = logger;
        }

        [HttpPost("custom")]
        public async Task<IActionResult> BuildCustomAnalytics([FromBody] DynamicAnalyticsRequest request)
        {
            if (request?.Rules == null || request.Rules.Count == 0)
                return BadRequest("At least one analytics rule is required.");

            var access = await CheckAccess(request.AgentId);
            if (!access.Allowed) return Denied(access);
            if (request.HoursAgo <= 0 || request.HoursAgo > GetMaxHours(access.PlanTier))
                return BadRequest($"The selected plan allows up to {GetMaxHours(access.PlanTier)} hours of history.");

            try
            {
                return Ok(await _analyticsService.BuildCustomAnalyticsAsync(request));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid custom analytics request: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not generate custom analytics for agent {AgentId}", request.AgentId);
                return StatusCode(500, "An internal server error occurred while processing analytics.");
            }
        }

        [HttpGet("agent/{agentId}/peaks")]
        public async Task<IActionResult> GetDailyPeaks(Guid agentId, [FromQuery] int daysAgo = 7)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);
            if (daysAgo <= 0 || daysAgo > GetMaxDays(access.PlanTier))
                return BadRequest($"The selected plan allows up to {GetMaxDays(access.PlanTier)} days of history.");

            return Ok(await _analyticsService.GetDailyPeakLoadsAsync(agentId, daysAgo));
        }

        [HttpGet("agent/{agentId}/network-trends")]
        public async Task<IActionResult> GetNetworkTrends(Guid agentId, [FromQuery] int hoursAgo = 24)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);
            if (hoursAgo <= 0 || hoursAgo > GetMaxHours(access.PlanTier))
                return BadRequest($"The selected plan allows up to {GetMaxHours(access.PlanTier)} hours of history.");

            return Ok(await _analyticsService.GetNetworkTrafficTrendsAsync(agentId, hoursAgo));
        }

        [HttpGet("agent/{agentId}/anomalies")]
        public async Task<IActionResult> GetCriticalAnomalies(Guid agentId, [FromQuery] int hoursAgo = 24)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);
            if (hoursAgo <= 0 || hoursAgo > GetMaxHours(access.PlanTier))
                return BadRequest($"The selected plan allows up to {GetMaxHours(access.PlanTier)} hours of history.");

            return Ok(await _analyticsService.GetCriticalAnomaliesAsync(agentId, hoursAgo));
        }

        [HttpGet("agent/{agentId}/disk-consumption")]
        public async Task<IActionResult> GetDiskConsumption(Guid agentId, [FromQuery] int daysAgo = 7)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);
            if (daysAgo <= 0 || daysAgo > GetMaxDays(access.PlanTier))
                return BadRequest($"The selected plan allows up to {GetMaxDays(access.PlanTier)} days of history.");

            var result = await _analyticsService.GetDiskConsumptionRateAsync(agentId, daysAgo);
            if (result == null || result.StartDiskSpace == 0) return NoContent();
            return Ok(result);
        }

        [HttpGet("agent/{agentId}/health-score")]
        public async Task<IActionResult> GetHealthScore(Guid agentId, [FromQuery] int hoursAgo = 24)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);
            if (hoursAgo <= 0 || hoursAgo > GetMaxHours(access.PlanTier))
                return BadRequest($"The selected plan allows up to {GetMaxHours(access.PlanTier)} hours of history.");

            var result = await _analyticsService.GetSystemHealthScoreAsync(agentId, hoursAgo);
            if (result == null || result.TotalMeasurements == 0) return NotFound("There are no measurements in the selected period.");
            return Ok(result);
        }

        [HttpGet("agent/{agentId}/timeline")]
        public async Task<IActionResult> GetMetricTimeline(
            Guid agentId,
            [FromQuery] int hoursAgo = 24,
            [FromQuery] int intervalMinutes = 60)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);
            if (hoursAgo <= 0 || hoursAgo > GetMaxHours(access.PlanTier))
                return BadRequest($"The selected plan allows up to {GetMaxHours(access.PlanTier)} hours of history.");
            if (intervalMinutes is not (5 or 15 or 60))
                return BadRequest("intervalMinutes must be 5, 15, or 60.");

            return Ok(await _analyticsService.GetMetricTimelineAsync(agentId, hoursAgo, intervalMinutes));
        }

        [HttpGet("agent/{agentId}/snapshot")]
        public async Task<IActionResult> GetMetricSnapshot(Guid agentId, [FromQuery] DateTime at)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);

            var selectedAt = DateTime.SpecifyKind(at, DateTimeKind.Utc);
            var now = DateTime.UtcNow;
            var oldestAllowed = now.AddHours(-GetMaxHours(access.PlanTier));
            if (selectedAt < oldestAllowed || selectedAt > now.AddMinutes(1))
                return BadRequest($"The selected timestamp must be within the last {GetMaxHours(access.PlanTier)} hours.");

            try
            {
                var snapshot = await _analyticsService.GetMetricSnapshotAsync(agentId, selectedAt, GetMaxHours(access.PlanTier));
                return snapshot == null ? NoContent() : Ok(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load metric snapshot for agent {AgentId} at {Timestamp}", agentId, selectedAt);
                return StatusCode(500, "Could not load metrics for the selected timestamp.");
            }
        }

        [HttpGet("agent/{agentId}/time-range")]
        public async Task<IActionResult> GetMetricTimeRange(Guid agentId)
        {
            var access = await CheckAccess(agentId);
            if (!access.Allowed) return Denied(access);

            try
            {
                var range = await _analyticsService.GetMetricTimeRangeAsync(agentId, GetMaxHours(access.PlanTier));
                return range == null ? NoContent() : Ok(range);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load metric time range for agent {AgentId}", agentId);
                return StatusCode(500, "Could not load the available metric time range.");
            }
        }

        private Task<AnalyticsAccessResult> CheckAccess(Guid agentId) =>
            _accessService.CheckAsync(Request.Headers.Authorization.ToString(), agentId, HttpContext.RequestAborted);

        private IActionResult Denied(AnalyticsAccessResult access) =>
            StatusCode(access.StatusCode, new { message = access.Message });

        private static int GetMaxHours(int planTier) => planTier switch
        {
            1 => 168,
            2 => 24,
            _ => 0
        };

        private static int GetMaxDays(int planTier) => planTier == 1 ? 7 : planTier == 2 ? 1 : 0;
    }
}
