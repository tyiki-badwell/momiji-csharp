using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.WebMidi;
using Momiji.Test.Run;
using System.Diagnostics;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        public HomeController(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<HomeController>();
        }

        [HttpPost]
        public IActionResult Note([FromBody]MIDIMessageEvent[] midiMessage, [FromServices]IRunner runner)
        {
            runner.Note(midiMessage);
            return Ok("{\"result\":\"OK\"}");
        }

        public IActionResult Start([FromServices]IRunner runner)
        {
            Param param = new Param();
            if (runner.Start(param))
            {
                ViewData["Message"] = "OK.";
            }
            else
            {
                ViewData["Message"] = "Failed.";
            }
            return View("Start");
        }

        
        public IActionResult Start2([FromServices]IRunner runner)
        {
            Param param = new Param();
            if (runner.Start2(param))
            {
                ViewData["Message"] = "OK.";
            }
            else
            {
                ViewData["Message"] = "Failed.";
            }
            return View("Start2");
        }

        public IActionResult Stop([FromServices]IRunner runner)
        {
            ViewData["Message"] = "Stop.";
            if (runner.Stop())
            {
                ViewData["Message"] = "OK.";
            }
            else
            {
                ViewData["Message"] = "Failed.";
            }
            return View("Stop");
        }
        
        public IActionResult Index()
        {
            return View("Index");
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
