using DTMS.DeliveryOrder.Presentation.Idempotency;
using FluentAssertions;

namespace DeliveryOrder.UnitTests;

public class IdempotencyHasherTests
{
    [Fact]
    public void SameArgs_ProduceSameHash()
    {
        var args = new List<object?> { new { Sku = "A", Qty = 5 }, Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var h1 = IdempotencyHasher.Compute("POST", "/api/x", args);
        var h2 = IdempotencyHasher.Compute("POST", "/api/x", args);

        h1.Should().Be(h2);
    }

    [Fact]
    public void DifferentFieldValue_ProducesDifferentHash()
    {
        var a = new List<object?> { new { Sku = "A", Qty = 5 } };
        var b = new List<object?> { new { Sku = "A", Qty = 6 } };

        IdempotencyHasher.Compute("POST", "/api/x", a)
            .Should().NotBe(IdempotencyHasher.Compute("POST", "/api/x", b));
    }

    [Fact]
    public void DifferentMethod_ProducesDifferentHash()
    {
        var args = new List<object?> { new { Sku = "A" } };

        IdempotencyHasher.Compute("POST", "/api/x", args)
            .Should().NotBe(IdempotencyHasher.Compute("PUT", "/api/x", args));
    }

    [Fact]
    public void DifferentPath_ProducesDifferentHash()
    {
        var args = new List<object?> { new { Sku = "A" } };

        IdempotencyHasher.Compute("POST", "/api/x", args)
            .Should().NotBe(IdempotencyHasher.Compute("POST", "/api/y", args));
    }

    [Fact]
    public void EmptyArgs_ProducesStableHash()
    {
        var h = IdempotencyHasher.Compute("POST", "/api/submit", new List<object?>());
        h.Should().NotBeNullOrEmpty();
        h.Length.Should().Be(64); // SHA256 hex
    }

    [Fact]
    public void NullArg_IsHashedAsNullLiteral()
    {
        var withNull = new List<object?> { (object?)null };
        var emptyList = new List<object?>();

        IdempotencyHasher.Compute("POST", "/x", withNull)
            .Should().NotBe(IdempotencyHasher.Compute("POST", "/x", emptyList));
    }
}
