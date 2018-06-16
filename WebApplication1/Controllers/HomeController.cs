using Microsoft.AspNetCore.Mvc;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebApplication1.Models;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks.Dataflow;
using static Momiji.Core.Opus;
using Momiji.Interop;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private static CancellationTokenSource processCancel;
        private static Task processTask;
        private IConfiguration Configuration { get; }

        public HomeController(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        private async Task Loop()
        {
            var ct = processCancel.Token;

            Trace.WriteLine("main loop start");

            try
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    int samplingRate = 48000;
                    int blockSize = 2880;

                    using (var pcm1 = new PinnedBuffer<float[]>(new float[blockSize * 2]))
                    using (var pcm2 = new PinnedBuffer<float[]>(new float[blockSize * 2]))
                    using (var out1 = new OpusOutputBuffer(5000))
                    using (var out2 = new OpusOutputBuffer(5000))
                    {
                        var vstToOpusInput = new BufferBlock<PinnedBuffer<float[]>>();
                        var vstToOpusOutput = new BufferBlock<PinnedBuffer<float[]>>();
                        var opusToFtlInput = new BufferBlock<OpusOutputBuffer>();
                        var opusToFtlOutput = new BufferBlock<OpusOutputBuffer>();

                        vstToOpusOutput.Post(pcm1);
                        vstToOpusOutput.Post(pcm2);
                        opusToFtlInput.Post(out1);
                        opusToFtlInput.Post(out2);

                        using (var vst = new Momiji.Core.Vst.AudioMaster(samplingRate, blockSize))
                        using (var encoder = new OpusEncoder(Opus.SamplingRate.Sampling48000, Opus.Channels.Stereo))
                        using (var ftl = new Momiji.Core.Ftl.FtlIngest($"{Configuration["MIXER_STREAM_KEY"]}"))
                        {
                            var effect = vst.AddEffect("Synth1 VST.dll");

                            effect.Run(
                                vstToOpusOutput,
                                vstToOpusInput
                            );
                            
                            encoder.Run(
                                vstToOpusInput,
                                vstToOpusOutput,
                                opusToFtlInput,
                                opusToFtlOutput
                            );
                            
                            ftl.Run(
                                opusToFtlOutput,
                                opusToFtlInput
                            );
                            
                            int a = 0;
                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    break;
                                }
                                Trace.WriteLine("wait:" + a++);
                                Thread.Sleep(1000);
                            }
                        }
                    }
                });
            }
            finally
            {
                Trace.WriteLine("main loop end");
            }
        }


        public IActionResult Start()
        {
            ViewData["Message"] = "Start.";
            if (processCancel == null)
            {
                processCancel = new CancellationTokenSource();
                processTask = Loop();
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
                        Trace.WriteLine("FtlIngest Process Exception:" + e.Message + " " + v.Message);
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

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
