using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.WebMidi;
using Momiji.Test.Run;

namespace WebApplication1.Controllers
{
    [Route("[controller]/[action]")]
    [ApiController]
    public class OperateController : ControllerBase
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        public OperateController(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<OperateController>();
        }
        /*
        [HttpPost]
        [ProducesResponseType(400)]
        public IActionResult Note([FromBody]MIDIMessageEvent[] midiMessage, [FromServices]IRunner runner)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            runner.Note(midiMessage);
            return Ok("{\"result\":\"OK\"}");
        }*/
    }
}
