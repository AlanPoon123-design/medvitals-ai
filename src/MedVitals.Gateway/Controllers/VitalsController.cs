using Microsoft.AspNetCore.Mvc;
using MedVitals.Gateway.Models;
using MedVitals.Gateway.Services;
using System.Threading.Tasks.Dataflow;

namespace MedVitals.Gateway.Controllers
{
    [ApiController]
    [Route("api/vitals")]
    public class VitalsController : ControllerBase
    {
        private readonly AnalyticsService _analyticsService;

        public VitalsController(AnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        // POST api/vitals
        // used by ESP32 C++ hardware telemetry and frontend single posts
        [HttpPost]
        public async Task<ActionResult<RiskAssessmentResult>> AssessVitals([FromBody] PatientVitals vitals)
        {
            if(vitals == null)
            {
                return BadRequest("Invalid vital sign payload.");
            }

            var result = await _analyticsService.ForwardToAnalyticsAsync(vitals);
            return Ok(result);
        }

        // POST api/vitals/batch-assess
        // used by Dataset File Scanner to process CSV batch records
        [HttpPost("batch-assess")]
        public async Task<ActionResult<List<RiskAssessmentResult>>> AssessVitalBatch([FromBody] List<PatientVitals> batch)
        {
            if(batch == null || !batch.Any()){
                return BadRequest("Batch payload is empty.");
            }

            var results = new List<RiskAssessmentResult>();
            foreach(var vitals in batch)
            {
                var res = await _analyticsService.ForwardToAnalyticsAsync(vitals);
                results.Add(res);
            }

            return Ok(results);
        }
    }
}