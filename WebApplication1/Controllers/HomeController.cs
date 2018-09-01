using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core;
using Momiji.Core.Ftl;
using Momiji.Core.H264;
using Momiji.Core.Opus;
using Momiji.Core.Vst;
using Momiji.Core.Wave;
using Momiji.Core.WebMidi;
using Momiji.Interop;
using Momiji.Interop.Opus;
using Momiji.Interop.Vst;
using Momiji.Interop.Wave;
using Momiji.Test.H264File;
using Momiji.Test.Run;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private static CancellationTokenSource processCancel;
        private static Task processTask;
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }

        private static BufferBlock<VstMidiEvent> midiEventInput = new BufferBlock<VstMidiEvent>();

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
            ViewData["Message"] = "Start.";
            if (processCancel == null)
            {
                processCancel = new CancellationTokenSource();
                processTask = runner.Loop(processCancel);
            }

            return View("Start");
        }

        
        public IActionResult Start2([FromServices]IRunner runner)
        {
            ViewData["Message"] = "Start2.";
            if (processCancel == null)
            {
                processCancel = new CancellationTokenSource();
                processTask = runner.Loop2(processCancel);
            }

            return View();
        }

        public IActionResult Stop()
        {
            ViewData["Message"] = "Stop.";
            if (processCancel != null)
            {
                processCancel.Cancel();
                try
                {
                    processTask.Wait();
                }
                catch (AggregateException e)
                {
                    foreach (var v in e.InnerExceptions)
                    {
                        Logger.LogInformation($"[home] Process Exception:{e.Message} {v.Message}");
                    }
                }
                finally
                {
                    processCancel.Dispose();
                    processCancel = null;
                }
            }
            return View();
        }
        
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
