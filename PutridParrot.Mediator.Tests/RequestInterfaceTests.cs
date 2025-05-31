using PutridParrot.Mediator;

namespace PutridParrot.Mediator.Tests;

[TestFixture]
public class RequestInterfaceTests
{
    [Test]
    public void IRequest_ShouldBeImplementable()
    {
        // Arrange & Act
        var request = new TestCommand();

        // Assert
        Assert.That(request, Is.InstanceOf<IRequest>());
    }

    [Test]
    public void IRequestWithResponse_ShouldBeImplementable()
    {
        // Arrange & Act
        var request = new TestQuery();

        // Assert
        Assert.That(request, Is.InstanceOf<IRequest<string>>());
        Assert.That(request, Is.InstanceOf<IRequest>());
    }

    [Test]
    public void IRequestWithResponse_ShouldSupportDifferentResponseTypes()
    {
        // Arrange & Act
        var intRequest = new IntQuery();
        var stringRequest = new StringQuery();
        var objectRequest = new ObjectQuery();

        // Assert
        Assert.That(intRequest, Is.InstanceOf<IRequest<int>>());
        Assert.That(stringRequest, Is.InstanceOf<IRequest<string>>());
        Assert.That(objectRequest, Is.InstanceOf<IRequest<object>>());
    }

    [Test]
    public void IRequest_WithRecord_ShouldWork()
    {
        // Arrange & Act
        var request = new RecordRequest(123, "Test");

        // Assert
        Assert.That(request, Is.InstanceOf<IRequest<RecordResponse>>());
        Assert.That(request.Id, Is.EqualTo(123));
        Assert.That(request.Name, Is.EqualTo("Test"));
    }

    [Test]
    public void IRequest_WithClass_ShouldWork()
    {
        // Arrange & Act
        var request = new ClassRequest { Id = 456, Description = "Test" };

        // Assert
        Assert.That(request, Is.InstanceOf<IRequest<string>>());
        Assert.That(request.Id, Is.EqualTo(456));
        Assert.That(request.Description, Is.EqualTo("Test"));
    }

    [Test]
    public void IRequest_WithStruct_ShouldWork()
    {
        // Arrange & Act
        var request = new StructRequest(100);

        // Assert
        Assert.That(request, Is.InstanceOf<IRequest<bool>>());
        Assert.That(request.Value, Is.EqualTo(100));
    }
}

// Test types
public class TestCommand : IRequest { }

public class TestQuery : IRequest<string> { }

public class IntQuery : IRequest<int> { }

public class StringQuery : IRequest<string> { }

public class ObjectQuery : IRequest<object> { }

public record RecordRequest(int Id, string Name) : IRequest<RecordResponse>;

public record RecordResponse(int Id, string Name, DateTime CreatedAt);

public class ClassRequest : IRequest<string>
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
}

public struct StructRequest : IRequest<bool>
{
    public int Value { get; }

    public StructRequest(int value)
    {
        Value = value;
    }
}
