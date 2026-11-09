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
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(IAnalyticsService analyticsService, ILogger<AnalyticsController> logger)
        {
            _analyticsService = analyticsService;
            _logger = logger;
        }

        [HttpPost("custom")]
        public async Task<IActionResult> BuildCustomAnalytics([FromBody] DynamicAnalyticsRequest request)
        {
            if (request == null || request.Rules == null || !request.Rules.Any())
            {
                return BadRequest("Запит повинен містити хоча б одне правило для аналітики.");
            }

            try
            {
                var result = await _analyticsService.BuildCustomAnalyticsAsync(request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Некоректний запит на динамічну аналітику: {Message}", ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Помилка генерації динамічної аналітики для агента {AgentId}", request.AgentId);
                return StatusCode(500, "Внутрішня помилка сервера при обробці запиту.");
            }
        }
        [HttpGet("agent/{agentId}/peaks")]
        public async Task<IActionResult> GetDailyPeaks(Guid agentId, [FromQuery] int daysAgo = 7)
        {
            if (daysAgo <= 0 || daysAgo > 365)
                return BadRequest("Період має бути від 1 до 365 днів.");

            var result = await _analyticsService.GetDailyPeakLoadsAsync(agentId, daysAgo);
            return Ok(result);
        }
        [HttpGet("agent/{agentId}/network-trends")]
        public async Task<IActionResult> GetNetworkTrends(Guid agentId, [FromQuery] int hoursAgo = 24)
        {
            if (hoursAgo <= 0 || hoursAgo > 720)
                return BadRequest("Період має бути від 1 до 720 годин.");

            var result = await _analyticsService.GetNetworkTrafficTrendsAsync(agentId, hoursAgo);
            return Ok(result);
        }
        [HttpGet("agent/{agentId}/anomalies")]
        public async Task<IActionResult> GetCriticalAnomalies(Guid agentId, [FromQuery] int hoursAgo = 24)
        {
            if (hoursAgo <= 0)
                return BadRequest("Період має бути більше 0 годин.");

            var result = await _analyticsService.GetCriticalAnomaliesAsync(agentId, hoursAgo);
        
            return Ok(result); 
        }
        [HttpGet("agent/{agentId}/disk-consumption")]
        public async Task<IActionResult> GetDiskConsumption(Guid agentId, [FromQuery] int daysAgo = 30)
        {
            var result = await _analyticsService.GetDiskConsumptionRateAsync(agentId, daysAgo);
            
            if (result == null || result.StartDiskSpace == 0)
            {
                return NoContent();
            }

            return Ok(result);
        }
        [HttpGet("agent/{agentId}/health-score")]
        public async Task<IActionResult> GetHealthScore(Guid agentId, [FromQuery] int hoursAgo = 24)
        {
            var result = await _analyticsService.GetSystemHealthScoreAsync(agentId, hoursAgo);
            
            if (result == null || result.TotalMeasurements == 0)
            {
                return NotFound($"Немає метрик для розрахунку здоров'я за останні {hoursAgo} годин.");
            }

            return Ok(result);
        }
    }
}