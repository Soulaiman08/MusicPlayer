using MusicPlayer.Utilities;

namespace MusicPlayer.Tests
{
    public class LogHelperTests
    {
        [Fact]
        public void Flush_MultipleCalls_NoCrash()
        {
            for (int i = 0; i < 1000; i++) LogHelper.LogInfo("msg " + i);
            LogHelper.Flush();
            LogHelper.Flush();
            LogHelper.Flush();
        }

        [Fact]
        public void LogInfo_AfterFlush_NoCrash()
        {
            LogHelper.Flush();
            LogHelper.LogInfo("after flush");
            LogHelper.Flush();
        }
    }
}