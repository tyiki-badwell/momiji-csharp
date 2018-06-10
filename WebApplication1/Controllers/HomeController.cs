using Microsoft.AspNetCore.Mvc;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WebApplication1.Models;
using Microsoft.Extensions.Configuration;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private static CancellationTokenSource processCancel;
        private static Task processTask;
        private static IConfiguration Configuration { get; set; }

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

                    var error = Momiji.Interop.Opus.OpusStatusCode.Unimplemented;

                    Momiji.Interop.Ftl.IngestParams param;
                    param.stream_key = $"{Configuration["MIXER_STREAM_KEY"]}";
                    param.video_codec = Momiji.Interop.Ftl.VideoCodec.FTL_VIDEO_H264;
                    param.audio_codec = Momiji.Interop.Ftl.AudioCodec.FTL_AUDIO_OPUS;
                    param.ingest_hostname = "auto";
                    param.fps_num = 24;
                    param.fps_den = 1;
                    param.peak_kbps = 0;
                    param.vendor_name = "momiji";
                    param.vendor_version = "0.0.1";

                    int frameSize = 960;
                    int maxDataByte = 3 * 1276;

                    using (var pcm = new Momiji.Interop.PinnedBuffer<short[]>(new short[frameSize * 1]))
                    using (var data = new Momiji.Interop.PinnedBuffer<byte[]>(new byte[maxDataByte]))
                    using (var vst = new Momiji.Core.Vst.Host())
                    using (var encoder =
                        Momiji.Interop.Opus.opus_encoder_create(
                            Momiji.Interop.Opus.SamplingRate.Sampling08000,
                            Momiji.Interop.Opus.Channels.Mono,
                            Momiji.Interop.Opus.OpusApplicationType.Audio,
                            out error
                        ))
                    using (var ftl = new Momiji.Core.Ftl.FtlIngest(ref param))
                    {
                        if (error == Momiji.Interop.Opus.OpusStatusCode.OK)
                        {
                            vst.AddEffect(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Synth1 VST.dll"));

                            int a = 0;
                            while (true)
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    ct.ThrowIfCancellationRequested();
                                }

                                int wrote = Momiji.Interop.Opus.opus_encode(
                                    encoder,
                                    pcm.AddrOfPinnedObject(),
                                    frameSize,
                                    data.AddrOfPinnedObject(),
                                    maxDataByte
                                    );

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
