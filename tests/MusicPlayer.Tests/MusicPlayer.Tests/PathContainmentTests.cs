using MusicPlayer.Utilities;

namespace MusicPlayer.Tests
{
    public class PathContainmentTests
    {
        [Theory]
        [InlineData(@"C:\Music\song.mp3", @"C:\Music", true)]
        [InlineData(@"C:\Music\Album\song.mp3", @"C:\Music", true)]
        [InlineData(@"C:\Music2\song.mp3", @"C:\Music", false)]
        [InlineData(@"C:\Music", @"C:\Music", true)]
        [InlineData(@"C:\Other\song.mp3", @"C:\Music", false)]
        [InlineData(@"C:\Music\", @"C:\Music", true)]
        public void IsWithin_BasicCases(string candidate, string container, bool expected)
        {
            Assert.Equal(expected, PathContainment.IsWithin(candidate, container));
        }

        [Fact]
        public void IsWithin_CaseInsensitive()
        {
            Assert.True(PathContainment.IsWithin(@"c:\music\song.mp3", @"C:\MUSIC"));
        }

        [Fact]
        public void IsWithin_DotDotCannotEscape()
        {
            Assert.False(PathContainment.IsWithin(@"C:\Music\..\Other\song.mp3", @"C:\Music"));
        }

        [Fact]
        public void IsWithin_EmptyOrNull()
        {
            Assert.False(PathContainment.IsWithin(null, @"C:\Music"));
            Assert.False(PathContainment.IsWithin(@"C:\Music\song.mp3", null));
            Assert.False(PathContainment.IsWithin("", @"C:\Music"));
            Assert.False(PathContainment.IsWithin(@"C:\Music\song.mp3", ""));
        }

        [Fact]
        public void IsWithin_Roots()
        {
            Assert.True(PathContainment.IsWithin(@"C:\", @"C:\"));
            Assert.True(PathContainment.IsWithin(@"C:\Windows", @"C:\"));
        }

        [Fact]
        public void IsStrictlyWithin_ExcludesEqual()
        {
            Assert.False(PathContainment.IsStrictlyWithin(@"C:\Music", @"C:\Music"));
            Assert.True(PathContainment.IsStrictlyWithin(@"C:\Music\song.mp3", @"C:\Music"));
        }

        [Fact]
        public void IsWithin_Unc()
        {
            Assert.True(PathContainment.IsWithin(@"\\server\share\folder\file.mp3", @"\\server\share\folder"));
            Assert.False(PathContainment.IsWithin(@"\\server\share2\file.mp3", @"\\server\share"));
        }
    }
}