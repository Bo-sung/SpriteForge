using System;
using FluentAssertions;
using SpriteForge.Core.Rendering;
using Xunit;

namespace PixelSprite.Tests;

public sealed class DirectionSchedulerTests
{
    [Fact]
    public void GetYaws_TwoDirections_ReturnsFrontAndBack()
    {
        DirectionScheduler.GetYaws(2).Should().Equal(0f, 180f);
    }

    [Fact]
    public void GetYaws_FourDirections_ReturnsCardinalAngles()
    {
        DirectionScheduler.GetYaws(4).Should().Equal(0f, 90f, 180f, 270f);
    }

    [Fact]
    public void GetYaws_EightDirections_ReturnsEvenlySpacedAngles()
    {
        DirectionScheduler.GetYaws(8)
            .Should().Equal(0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f);
    }

    [Fact]
    public void GetYaws_ThreeDirections_Throws()
    {
        Action act = () => DirectionScheduler.GetYaws(3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetYaws_ZeroDirections_Throws()
    {
        Action act = () => DirectionScheduler.GetYaws(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
