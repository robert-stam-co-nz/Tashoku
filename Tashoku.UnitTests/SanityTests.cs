using Xunit;

namespace Tashoku.UnitTests
{
    public class SanityTests
    {
        [Fact]
        public void BasicMath_Works()
        {
            Assert.Equal(4, 2 + 2);
        }

        [Fact]
        public void Placeholder_Check()
        {
            // Placeholder test to ensure test discovery works reliably across environments
            Assert.True(true);
        }
    }
}
