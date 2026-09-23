using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mindscape.Raygun4Net.NetCore.Tests
{
  [TestFixture]
  public class RaygunMessageBuilderTests
  {
    private RaygunSettings _settings;
    private RaygunMessageBuilder _builder;

    [SetUp]
    public void SetUp()
    {
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;
      _settings = new RaygunSettings();
      _builder = RaygunMessageBuilder.New(_settings);
    }

    [Test]
    public void New()
    {
      Assert.That(_builder, Is.Not.Null);
    }

    [Test]
    public void SetVersion()
    {
      var builder = _builder.SetVersion("Custom Version");
      Assert.That(_builder, Is.EqualTo(builder));

      var message = _builder.Build();
      Assert.That("Custom Version", Is.EqualTo(message.Details.Version));
    }

    [Test]
    public void SetTimeStamp()
    {
      var time = new DateTime(2015, 2, 16);
      var message = _builder.SetTimeStamp(time).Build();
      Assert.That(time, Is.EqualTo(message.OccurredOn));
    }

    [Test]
    public void SetNullTimeStamp()
    {
      var message = _builder.SetTimeStamp(null).Build();
      Assert.That((DateTime.UtcNow - message.OccurredOn).TotalSeconds < 1, Is.True);
    }

    [Test]
    public void HasMachineName()
    {
      var message = _builder.SetMachineName(Environment.MachineName).Build();

      Assert.That(message.Details, Is.Not.Null);
      Assert.That(message.Details.MachineName, Is.Not.Null);
    }

    [Test]
    public void HasEnvironmentInformation()
    {
      var message = _builder.SetEnvironmentDetails().Build();

      Assert.That(message.Details, Is.Not.Null);
      Assert.That(message.Details.Environment, Is.Not.Null);
      Assert.That(message.Details.Environment.Architecture, Is.Not.Empty);
      
      Assert.That(message.Details.Environment.WindowBoundsHeight, Is.GreaterThanOrEqualTo(0));
      Assert.That(message.Details.Environment.WindowBoundsWidth, Is.GreaterThanOrEqualTo(0));

      Assert.That(message.Details.Environment.Cpu, Is.Not.Empty);

      Assert.That(message.Details.Environment.ProcessorCount, Is.GreaterThanOrEqualTo(1));
      Assert.That(message.Details.Environment.OSVersion, Is.Not.Empty);
      Assert.That(message.Details.Environment.Locale, Is.Not.Empty);

      Assert.That(message.Details.Environment.DiskSpaceFree, Is.Not.Null);
      Assert.That(message.Details.Environment.DiskSpaceFree.Any(), Is.True);
      Assert.That(message.Details.Environment.DiskSpaceFree.All(a => a > 0), Is.True);
    }

    [Test]
    public void HasEnvironmentMemoryInformation()
    {
      var message = _builder.SetEnvironmentDetails().Build();

      Assert.That(message.Details.Environment.AvailablePhysicalMemory, Is.Not.Zero);
      Assert.That(message.Details.Environment.TotalPhysicalMemory, Is.Not.Zero);
      Assert.That(message.Details.Environment.AvailableVirtualMemory, Is.Not.Zero);
      Assert.That(message.Details.Environment.TotalVirtualMemory, Is.Not.Zero);
    }

    [Test]
    public void EnvironmentBuild_WhenRefreshInProgressOnAnotherThread_ReturnsCachedValuesWithoutWaiting()
    {
      RaygunEnvironmentMessageBuilder.Build(_settings);
      var staleUpdate = DateTime.UtcNow.AddMinutes(-5);
      RaygunEnvironmentMessageBuilder.LastUpdate = staleUpdate;

      RaygunEnvironmentMessageBuilder.Semaphore.Wait();
      try
      {
        var build = Task.Run(() => RaygunEnvironmentMessageBuilder.Build(_settings));

        build.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("Build should not wait for another thread's refresh");
        build.Result.Should().NotBeNull();
        RaygunEnvironmentMessageBuilder.LastUpdate.Should().Be(staleUpdate);
      }
      finally
      {
        RaygunEnvironmentMessageBuilder.Semaphore.Release();
      }
    }

    [Test]
    public void EnvironmentBuild_WhenFirstRefreshInProgressOnAnotherThread_ReturnsEmptyDiskSpaceWithoutThrowing()
    {
      // Simulates the state of a fresh process: no refresh has completed yet, so the cached disk space is unset
      GetCachedEnvironmentMessage().DiskSpaceFree = null;
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;

      RaygunEnvironmentMessageBuilder.Semaphore.Wait();
      try
      {
        var build = Task.Run(() => RaygunEnvironmentMessageBuilder.Build(_settings));

        build.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("Build should not wait for another thread's refresh");
        build.Result.DiskSpaceFree.Should().NotBeNull().And.BeEmpty();
      }
      finally
      {
        RaygunEnvironmentMessageBuilder.Semaphore.Release();
        RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;
      }
    }

    [Test]
    public void EnvironmentBuild_WhenRefreshInProgressOnAnotherThread_DoesNotReleaseSemaphore()
    {
      RaygunEnvironmentMessageBuilder.Build(_settings);
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

      RaygunEnvironmentMessageBuilder.Semaphore.Wait();
      try
      {
        Task.Run(() => RaygunEnvironmentMessageBuilder.Build(_settings)).Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        RaygunEnvironmentMessageBuilder.Semaphore.CurrentCount.Should().Be(0);
      }
      finally
      {
        RaygunEnvironmentMessageBuilder.Semaphore.Release();
      }
    }

    [Test]
    public void EnvironmentBuild_WhenCacheIsStaleAndNoRefreshInProgress_RefreshesCache()
    {
      RaygunEnvironmentMessageBuilder.Build(_settings);
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

      RaygunEnvironmentMessageBuilder.Build(_settings);

      RaygunEnvironmentMessageBuilder.LastUpdate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Test]
    public void EnvironmentBuild_WhenCacheIsFresh_DoesNotRefreshOrHoldSemaphore()
    {
      RaygunEnvironmentMessageBuilder.Build(_settings);
      var freshUpdate = DateTime.UtcNow.AddSeconds(-30);
      RaygunEnvironmentMessageBuilder.LastUpdate = freshUpdate;

      RaygunEnvironmentMessageBuilder.Build(_settings);

      RaygunEnvironmentMessageBuilder.LastUpdate.Should().Be(freshUpdate);
      RaygunEnvironmentMessageBuilder.Semaphore.CurrentCount.Should().Be(1);
    }

    [Test]
    public void EnvironmentBuild_WhenRefreshInProgressOnAnotherThread_ReturnsPreviouslyCachedValues()
    {
      RaygunEnvironmentMessageBuilder.Build(_settings);
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

      RaygunEnvironmentMessageBuilder.Semaphore.Wait();
      try
      {
        var build = Task.Run(() => RaygunEnvironmentMessageBuilder.Build(_settings));
        build.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        build.Result.OSVersion.Should().NotBeNullOrEmpty();
        build.Result.ProcessorCount.Should().BeGreaterThan(0);
        build.Result.TotalPhysicalMemory.Should().NotBe(0);
      }
      finally
      {
        RaygunEnvironmentMessageBuilder.Semaphore.Release();
      }
    }

    [Test]
    public void EnvironmentBuild_AfterSkippedRefresh_RefreshesOnceSemaphoreIsFree()
    {
      RaygunEnvironmentMessageBuilder.Build(_settings);
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

      RaygunEnvironmentMessageBuilder.Semaphore.Wait();
      try
      {
        Task.Run(() => RaygunEnvironmentMessageBuilder.Build(_settings)).Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
      }
      finally
      {
        RaygunEnvironmentMessageBuilder.Semaphore.Release();
      }

      RaygunEnvironmentMessageBuilder.Build(_settings);

      RaygunEnvironmentMessageBuilder.LastUpdate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task EnvironmentBuild_WhenFirstBuildIsCalledConcurrently_NeverThrowsAndReleasesSemaphore()
    {
      GetCachedEnvironmentMessage().DiskSpaceFree = null;
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;

      var builds = Enumerable.Range(0, 50)
                             .Select(_ => Task.Run(() => RaygunEnvironmentMessageBuilder.Build(_settings)))
                             .ToArray();

      var results = await Task.WhenAll(builds);

      results.Should().OnlyContain(r => r.DiskSpaceFree != null);
      RaygunEnvironmentMessageBuilder.Semaphore.CurrentCount.Should().Be(1);
      RaygunEnvironmentMessageBuilder.LastUpdate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Test]
    public void EnvironmentBuild_AfterRefresh_ReleasesSemaphore()
    {
      RaygunEnvironmentMessageBuilder.Build(_settings);

      RaygunEnvironmentMessageBuilder.Semaphore.CurrentCount.Should().Be(1);
    }

    [Test]
    public void EnvironmentBuild_ModifyingReturnedDiskSpace_DoesNotAffectLaterMessages()
    {
      var first = RaygunEnvironmentMessageBuilder.Build(_settings);
      var diskCount = first.DiskSpaceFree.Count;

      first.DiskSpaceFree.Add(123);

      RaygunEnvironmentMessageBuilder.Build(_settings).DiskSpaceFree.Should().HaveCount(diskCount);
    }

    private static RaygunEnvironmentMessage GetCachedEnvironmentMessage()
    {
      return (RaygunEnvironmentMessage)typeof(RaygunEnvironmentMessageBuilder)
        .GetField("CachedMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null)!;
    }

    // Response tests

    [Test]
    public void ResponseIsNullForNonWebExceptions()
    {
      var exception = new NullReferenceException("The thing is null");
      _builder.SetExceptionDetails(exception);
      var message = _builder.Build();
      Assert.That(message.Details.Response, Is.Null);
    }
    
    [Test]
    public void Customise_ExistingMessage_CorrectlyModifiesProperties()
    {
      var settings = new RaygunSettings();
      var builder = RaygunMessageBuilder.New(settings)
                                        .SetVersion("1.0.0")
                                        .SetEnvironmentDetails()
                                        .Customise(m =>
                                        {
                                          m.Details.Version = "2.0.0";
                                          m.Details.Environment.Architecture = "BANANA";
                                        });
      
      var modifiedMessage = builder.Build();

      modifiedMessage.Details.Version.Should().Be("2.0.0");
      modifiedMessage.Details.Environment.Architecture.Should().Be("BANANA");
    }
    
    [Test]
    public void SetEnvironmentDetails_WithEnvironmentVariables_ExactMatch()
    {
      var settings = new RaygunSettings
      {
        EnvironmentVariables = new List<string>
        {
          "PATH"
        }
      };
      var builder = RaygunMessageBuilder.New(settings)
                                        .SetEnvironmentDetails();
      
      var msg = builder.Build();

      msg.Details.Environment.EnvironmentVariables.Keys.Cast<string>().Should().Contain(s => s.Equals("path", StringComparison.OrdinalIgnoreCase));
    }
    
    [Test]
    public void SetEnvironmentDetails_WithEnvironmentVariables_StartsWith()
    {
      Environment.SetEnvironmentVariable("TEST_One", "1");
      Environment.SetEnvironmentVariable("TEST_Two", "2");
      Environment.SetEnvironmentVariable("TEST_Three", "3");
      
      var settings = new RaygunSettings
      {
        EnvironmentVariables = new List<string>
        {
          "TEST_*"
        }
      };
      var builder = RaygunMessageBuilder.New(settings)
                                        .SetEnvironmentDetails();
      
      var msg = builder.Build();

      msg.Details.Environment.EnvironmentVariables.Keys.Cast<string>()
         .Should().HaveCount(3)
         .And.Contain(new []
      {
        "TEST_One", 
        "TEST_Two", 
        "TEST_Three"
      });
    }
    
    [Test]
    public void SetEnvironmentDetails_WithEnvironmentVariables_EndsWith()
    {
      Environment.SetEnvironmentVariable("One_Banana", "1");
      Environment.SetEnvironmentVariable("Two_Banana", "2");
      Environment.SetEnvironmentVariable("Three_Banana", "3");
      
      var settings = new RaygunSettings
      {
        EnvironmentVariables = new List<string>
        {
          "*_Banana"
        }
      };
      var builder = RaygunMessageBuilder.New(settings)
                                        .SetEnvironmentDetails();
      
      var msg = builder.Build();

      msg.Details.Environment.EnvironmentVariables.Keys.Cast<string>()
         .Should().HaveCount(3)
         .And.Contain(new []
      {
        "One_Banana", 
        "Two_Banana", 
        "Three_Banana"
      });
    }
    
    [Test]
    public void SetEnvironmentDetails_WithEnvironmentVariables_Contains()
    {
      Environment.SetEnvironmentVariable("ONE_Banana_Two", "1");
      Environment.SetEnvironmentVariable("Two_Test_Three", "2");
      Environment.SetEnvironmentVariable("ThreeBananaFour", "3");
      
      var settings = new RaygunSettings
      {
        EnvironmentVariables = new List<string>
        {
          "*_Banana*"
        }
      };
      
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;
      var builder = RaygunMessageBuilder.New(settings)
                                        .SetEnvironmentDetails();
      
      var msg = builder.Build();

      msg.Details.Environment.EnvironmentVariables.Keys.Cast<string>()
         .Should().HaveCount(1)
         .And.Contain(new []
      {
        "ONE_Banana_Two"
      });
    }
    
    [Test]
    public void SetEnvironmentDetails_WithEnvironmentVariables_Star_ShouldReturnNothing()
    {
      Environment.SetEnvironmentVariable("ONE_Banana_Two", "1");
      Environment.SetEnvironmentVariable("Two_Test_Three", "2");
      Environment.SetEnvironmentVariable("ThreeBananaFour", "3");
      
      var settings = new RaygunSettings
      {
        EnvironmentVariables = new List<string>
        {
          "*",
          "**",
          "***",
          "* *",
        }
      };
      
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;
      var builder = RaygunMessageBuilder.New(settings)
                                        .SetEnvironmentDetails();
      
      var msg = builder.Build();

      msg.Details.Environment.EnvironmentVariables.Keys.Cast<string>()
         .Should().HaveCount(0);
    }
    
    [TestCase("LEMON", "lemon")]
    [TestCase("kIwIfRuIt", "KIWIFRUIT")]
    [TestCase("WAterMeLON", "water*")]
    [TestCase("gRaPE", "*ape")]
    [TestCase("DraGonFrUiT", "*nfr*")]
    public void SetEnvironmentDetails_WithEnvironmentVariablesWithDifferentCasing_ShouldIgnoreCaseAndReturn(string key, string search)
    {
      Environment.SetEnvironmentVariable("lOnGan", "1");
      Environment.SetEnvironmentVariable(key, "2");
      Environment.SetEnvironmentVariable("aPrIcOt", "3");
      
      var settings = new RaygunSettings
      {
        EnvironmentVariables = new List<string>
        {
          search
        }
      };
      
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;
      var builder = RaygunMessageBuilder.New(settings)
                                        .SetEnvironmentDetails();
      
      var msg = builder.Build();

      msg.Details.Environment.EnvironmentVariables.Keys.Cast<string>()
         .Should().HaveCount(1)
         .And.Contain(new []
         {
           key
         });
    }
  }
}
