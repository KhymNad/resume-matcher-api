using Microsoft.AspNetCore.Mvc;

namespace ResumeMatcherAPI.Controllers
{
    // attribute marks the class as a Web API controller.
    // enables automatic model validation and better routing behavior.
    [ApiController]

    // defines the base route for this controller: "api/resume".
    // [controller] is a token that uses the controller's class name minus "Controller".
    [Route("api/[controller]")]
    public class ResumeController : ControllerBase // Inherits from ControllerBase to get basic web API functionality
    {
        /// <summary>
        /// GET: /api/resume/health
        /// A simple health check endpoint to verify the API is running.
        /// Returns a 200 OK with the message "API is running".
        /// </summary>

        // This attribute maps GET requests to /api/resume/health to this method.
        [HttpGet("health")]
        public IActionResult Health()
        {
            // Ok() returns a 200 HTTP response with the given message
            return Ok("API is running");
        }
    }
}
