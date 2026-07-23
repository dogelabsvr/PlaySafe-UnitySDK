using Newtonsoft.Json;
using NUnit.Framework;
using _DL.PlaySafe;

namespace _DL.PlaySafe.Tests
{
    public class RemoteConfigCompatTests
    {
        [Test]
        public void OldBackendPayload_WithoutEchoFields_YieldsNulls()
        {
            const string json = "{\"samplingRate\":0.5,\"isSmartSamplingEnabled\":true," +
                                "\"audioSilenceThreshold\":0.001,\"playerStatsExpiryInDays\":30," +
                                "\"sessionPulseIntervalSeconds\":60}";
            var config = JsonConvert.DeserializeObject<RemoteConfigVoiceAIData>(json);
            Assert.IsNull(config.EchoDetectionEnabled);
            Assert.IsNull(config.EchoAnnotateOnly);
            Assert.IsNull(config.EchoDropThreshold);
            Assert.AreEqual(0.5f, config.SamplingRate);
            // The client applies `?? default` on nulls: enabled=true, annotateOnly=true, threshold=0.85
            Assert.AreEqual(true, config.EchoDetectionEnabled ?? true);
            Assert.AreEqual(true, config.EchoAnnotateOnly ?? true);
            Assert.AreEqual(0.85f, config.EchoDropThreshold ?? 0.85f);
        }

        [Test]
        public void NewBackendPayload_WithEchoFields_Parses()
        {
            const string json = "{\"samplingRate\":0.5,\"audioSilenceThreshold\":0.001," +
                                "\"echoDetectionEnabled\":true,\"echoAnnotateOnly\":false,\"echoDropThreshold\":0.9}";
            var config = JsonConvert.DeserializeObject<RemoteConfigVoiceAIData>(json);
            Assert.AreEqual(true, config.EchoDetectionEnabled);
            Assert.AreEqual(false, config.EchoAnnotateOnly);
            Assert.AreEqual(0.9f, config.EchoDropThreshold);
        }

        [Test]
        public void UnknownExtraFields_DoNotBreakParsing()
        {
            const string json = "{\"samplingRate\":1.0,\"someFutureField\":\"x\",\"echoDropThreshold\":0.7}";
            var config = JsonConvert.DeserializeObject<RemoteConfigVoiceAIData>(json);
            Assert.AreEqual(0.7f, config.EchoDropThreshold);
        }
    }
}
