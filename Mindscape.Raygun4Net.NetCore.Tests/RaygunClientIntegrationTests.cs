using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mindscape.Raygun4Net.NetCore.Tests
{
  [TestFixture]
  public class RaygunClientIntegrationTests
  {
    private HttpClient _httpClient = null!;
    private MockHttpHandler _mockHttp = null!;

    public class BananaClient : RaygunClient
    {
      public BananaClient(RaygunSettings settings, HttpClient httpClient) : base(settings, httpClient)
      {
      }
    }

    [SetUp]
    public void Init()
    {
      _mockHttp = new MockHttpHandler();
    }

    [Test]
    public async Task SendInBackground_ShouldNotBlock()
    {
      _mockHttp.When(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"))
        .Respond(x => 
        {
          x.Latency(NetworkLatency.Between(5, 15));
          x.Body("OK");
          x.StatusCode(HttpStatusCode.Accepted);
        }).Verifiable();

      _httpClient = new HttpClient(_mockHttp);

      // This test runs using a mocked http client so we don't need a real API key, if you want to run this
      // and have the data sent to Raygun, remove the `httpClient` parameter from the RaygunClient constructor
      // and set a real API key. This will then send the data to Raygun and you can verify that it is being sent.
      var client = new BananaClient(new RaygunSettings
      {
        ApiKey = "banana"
      }, _httpClient);

      var stopwatch = new Stopwatch();
      
      for (int i = 0; i < 50; i++)
      {
        try
        {
          throw new Exception("Test Exception to ensure SendInBackground is not blocking");
        }
        catch (Exception e)
        {
          await client.SendInBackground(e);
        }
      }
      
      var elapsed = stopwatch.ElapsedMilliseconds;
      
      // 50 * 300 = 15000ms
      // If the requests were blocking it would take minimum 15 seconds at minimum 300ms per request to send each one
      // So we can assume that the requests are being sent in parallel, this should be a lot less than 15 seconds
      Assert.That(elapsed, Is.LessThan(100));
      
      Console.WriteLine("Elapsed: " + elapsed + "ms");

      // Delay 1 second to give it time to send all the messages
      await Task.Delay(1000);

      // Verify that the request was sent 50 times
      await _mockHttp.VerifyAsync(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"), IsSent.Exactly(50));
    }

    [Test]
    public async Task SendInBackground_WhenFirstEnvironmentRefreshInProgress_StillSendsMessage()
    {
      _mockHttp.When(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"))
        .Respond(x =>
        {
          x.Body("OK");
          x.StatusCode(HttpStatusCode.Accepted);
        }).Verifiable();

      _httpClient = new HttpClient(_mockHttp);

      var client = new BananaClient(new RaygunSettings
      {
        ApiKey = "banana"
      }, _httpClient);

      // Simulates #584: another thread is stuck in the first environment refresh (e.g. probing an unreachable drive)
      var cachedMessage = (RaygunEnvironmentMessage)typeof(RaygunEnvironmentMessageBuilder)
        .GetField("CachedMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null)!;
      cachedMessage.DiskSpaceFree = null;
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;

      RaygunEnvironmentMessageBuilder.Semaphore.Wait();
      try
      {
        var send = Task.Run(() => client.SendInBackground(new Exception("Test Exception while environment refresh is in progress")));

        (await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(send, "SendInBackground should not wait for another thread's refresh");
        await send;

        // Delay 1 second to give it time to send the message
        await Task.Delay(1000);
      }
      finally
      {
        RaygunEnvironmentMessageBuilder.Semaphore.Release();
        RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;
      }

      await _mockHttp.VerifyAsync(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"), IsSent.Exactly(1));
    }

    [Test]
    public async Task SendAsync_WhenFirstEnvironmentRefreshInProgress_StillSendsMessage()
    {
      _mockHttp.When(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"))
        .Respond(x =>
        {
          x.Body("OK");
          x.StatusCode(HttpStatusCode.Accepted);
        }).Verifiable();

      _httpClient = new HttpClient(_mockHttp);

      var client = new BananaClient(new RaygunSettings
      {
        ApiKey = "banana"
      }, _httpClient);

      var cachedMessage = (RaygunEnvironmentMessage)typeof(RaygunEnvironmentMessageBuilder)
        .GetField("CachedMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null)!;
      cachedMessage.DiskSpaceFree = null;
      RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;

      RaygunEnvironmentMessageBuilder.Semaphore.Wait();
      try
      {
        var send = Task.Run(() => client.SendAsync(new Exception("Test Exception while environment refresh is in progress")));

        (await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(send, "SendAsync should not wait for another thread's refresh");
        await send;
      }
      finally
      {
        RaygunEnvironmentMessageBuilder.Semaphore.Release();
        RaygunEnvironmentMessageBuilder.LastUpdate = DateTime.MinValue;
      }

      // SendAsync swallows exceptions by default, so a failure to build the message would only show as nothing being sent
      await _mockHttp.VerifyAsync(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"), IsSent.Exactly(1));
    }

    [Test]
    public async Task SendInBackground_ShouldFail_WhenMaxTasksIsZero()
    {
      _mockHttp.When(match => match.Method(HttpMethod.Post)
          .RequestUri("https://api.raygun.com/entries"))
        .Respond(x => 
        {
          x.Latency(NetworkLatency.Between(5, 15));
          x.Body("OK");
          x.StatusCode(HttpStatusCode.Accepted);
        }).Verifiable();

      _httpClient = new HttpClient(_mockHttp);

      // This test runs using a mocked http client so we don't need a real API key, if you want to run this
      // and have the data sent to Raygun, remove the `httpClient` parameter from the RaygunClient constructor
      // and set a real API key. This will then send the data to Raygun and you can verify that it is being sent.
      var client = new BananaClient(new RaygunSettings
      {
        ApiKey = "banana",
        BackgroundMessageWorkerCount = 0
      }, _httpClient);

      var stopwatch = new Stopwatch();
      
      for (int i = 0; i < 50; i++)
      {
        try
        {
          throw new Exception("Test Exception to ensure SendInBackground is not blocking");
        }
        catch (Exception e)
        {
          await client.SendInBackground(e);
        }
      }
      
      var elapsed = stopwatch.ElapsedMilliseconds;
      
      // 50 * 300 = 15000ms
      // If the requests were blocking it would take minimum 15 seconds at minimum 300ms per request to send each one
      // So we can assume that the requests are being sent in parallel, this should be a lot less than 15 seconds
      Assert.That(elapsed, Is.LessThan(100));
      
      Console.WriteLine("Elapsed: " + elapsed + "ms");

      // Delay 1 second to give it time to send all the messages
      await Task.Delay(1000);

      // Verify that the request wasn't sent because there was no worker
      await _mockHttp.VerifyAsync(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"), IsSent.Exactly(0));
    }
    
    [Test]
    [NonParallelizable]
    public async Task SendInBackground_WithLowQueueMax_DoesNotSendAllRequests()
    {
      Environment.SetEnvironmentVariable("RAYGUN_MESSAGE_QUEUE_MAX", "10");
      
      _mockHttp.When(match => match.Method(HttpMethod.Post)
          .RequestUri("https://api.raygun.com/entries"))
        .Respond(x => 
        {
          x.Latency(NetworkLatency.Between(5, 15));
          x.Body("OK");
          x.StatusCode(HttpStatusCode.Accepted);
        }).Verifiable();

      _httpClient = new HttpClient(_mockHttp);

      // This test runs using a mocked http client so we don't need a real API key, if you want to run this
      // and have the data sent to Raygun, remove the `httpClient` parameter from the RaygunClient constructor
      // and set a real API key. This will then send the data to Raygun and you can verify that it is being sent.
      var client = new BananaClient(new RaygunSettings
      {
        ApiKey = "banana"
      }, _httpClient);

      var stopwatch = new Stopwatch();
      
      for (int i = 0; i < 50; i++)
      {
        try
        {
          throw new Exception("Test Exception to ensure SendInBackground is not blocking");
        }
        catch (Exception e)
        {
          await client.SendInBackground(e);
        }
      }
      
      var elapsed = stopwatch.ElapsedMilliseconds;
      
      // 50 * 300 = 15000ms
      // If the requests were blocking it would take minimum 15 seconds at minimum 300ms per request to send each one
      // So we can assume that the requests are being sent in parallel, this should be a lot less than 15 seconds
      Assert.That(elapsed, Is.LessThan(100));
      
      Console.WriteLine("Elapsed: " + elapsed + "ms");

      // Delay 1 second to give it time to send all the messages
      await Task.Delay(1000);

      // Verify that the request was sent 50 times
      await _mockHttp.VerifyAsync(match => match.Method(HttpMethod.Post)
        .RequestUri("https://api.raygun.com/entries"), IsSent.AtMost(49));
      
      Environment.SetEnvironmentVariable("RAYGUN_MESSAGE_QUEUE_MAX", null);
    }
  }
}