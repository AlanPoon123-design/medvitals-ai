using MedVitals.Gateway.Authentication;
using MedVitals.Gateway.Models;
using MedVitals.Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedVitals.Gateway.Controllers
{
    [ApiController]
    [Route("api/vitals")]
    public class VitalsController : ControllerBase
    {
        private readonly AnalyticsService _analyticsService;
        private readonly LatestAssessmentStore _latestStore;

        public VitalsController(AnalyticsService analyticsService, LatestAssessmentStore latestStore)
        {
            _analyticsService = analyticsService;
            _latestStore = latestStore;
        }

        // POST api/vitals
        // used by ESP32 C++ hardware telemetry (X-Api-Key = Auth:DeviceApiKey)
        // [ApiController] already returns 400 for a null/invalid body.
        [HttpPost]
        [Authorize(Policy = ApiKeyDefaults.DevicePolicy)]
        public async Task<ActionResult<RiskAssessmentResult>> AssessVitals([FromBody] PatientVitals vitals, CancellationToken ct)
        {
            try
            {
                var result = await _analyticsService.ForwardToAnalyticsAsync(vitals, ct);
                _latestStore.Set(result);
                return Ok(result);
            }
            catch (AnalyticsUnavailableException ex)
            {
                return AnalyticsProblem(ex);
            }
        }

        // GET api/vitals/{patientId}
        // polled by the dashboard for the latest hardware reading (X-Api-Key = Auth:DashboardApiKey)
        [HttpGet("{patientId}")]
        [Authorize(Policy = ApiKeyDefaults.DashboardPolicy)]
        public ActionResult<RiskAssessmentResult> GetLatest(string patientId)
        {
            return _latestStore.TryGet(patientId, out var result) && result is not null
                ? Ok(result)
                : NotFound();
        }

        // POST api/vitals/batch-assess
        // used by Dataset File Scanner to process CSV batch records in a single round trip
        // (X-Api-Key = Auth:DashboardApiKey)
        [HttpPost("batch-assess")]
        [Authorize(Policy = ApiKeyDefaults.DashboardPolicy)]
        public async Task<ActionResult<List<RiskAssessmentResult>>> AssessVitalBatch([FromBody] List<PatientVitals> batch, CancellationToken ct)
        {
            if (batch.Count == 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Batch payload is empty."
                });
            }

            try
            {
                var results = await _analyticsService.AssessBatchAsync(batch, ct);
                return Ok(results);
            }
            catch (AnalyticsUnavailableException ex)
            {
                return AnalyticsProblem(ex);
            }
        }

        private ObjectResult AnalyticsProblem(AnalyticsUnavailableException ex)
        {
            var status = (int)ex.StatusCode;
            return StatusCode(status, new ProblemDetails
            {
                Status = status,
                Title = status == StatusCodes.Status400BadRequest
                    ? "Invalid vital-sign payload"
                    : "Analytics engine unavailable",
                Detail = ex.Message
            });
        }
    }
}
