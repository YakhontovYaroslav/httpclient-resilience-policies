using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Contrib.WaitAndRetry;

namespace Dodo.HttpClientResiliencePolicies.RetryPolicy
{
	public class RetryPolicySettings : IRetryPolicySettings
	{
		private readonly int _retryCount;
		int IRetryPolicySettings.RetryCount => _retryCount;

		private readonly Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> _sleepDurationProvider;
		Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> IRetryPolicySettings.SleepDurationProvider
		{
			get => (retryCount, response, context) =>
			{
				var serverWaitDuration = getServerWaitDuration(response);
				if (serverWaitDuration.HasValue)
				{
					return serverWaitDuration.Value;
				}

				return _sleepDurationProvider(retryCount, response, context);
			};
		}

		Func<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context, Task> IRetryPolicySettings.OnRetryForPolly
		{
			get => (response, span, retryCount, context) =>
			{
				OnRetry(response, span);
				return Task.CompletedTask;
			};
		}

		public Action<DelegateResult<HttpResponseMessage>, TimeSpan> OnRetry { get; set; }

		public RetryPolicySettings()
		{
			_sleepDurationProvider = SleepDurationProvider.Jitter(
				Defaults.Retry.RetryCount,
				TimeSpan.FromMilliseconds(Defaults.Retry.MedianFirstRetryDelayInMilliseconds));

			OnRetry = DoNothingOnRetry;
			_retryCount = Defaults.Retry.RetryCount;
		}

		private RetryPolicySettings(
			int retryCount,
			Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> sleepDurationProvider)
		{
			_sleepDurationProvider = sleepDurationProvider;
			OnRetry = DoNothingOnRetry;
			_retryCount = retryCount;
		}

		private static readonly Action<DelegateResult<HttpResponseMessage>, TimeSpan> DoNothingOnRetry = (_, __) => { };

		public static RetryPolicySettings Constant(int retryCount)
		{
			return Constant(retryCount, TimeSpan.FromMilliseconds(Defaults.Retry.InitialDelayMilliseconds));
		}

		public static RetryPolicySettings Constant(int retryCount, TimeSpan delay)
		{
			return new RetryPolicySettings(retryCount, SleepDurationProvider.Constant(retryCount, delay));
		}

		public static RetryPolicySettings Linear(int retryCount)
		{
			return Linear(retryCount, TimeSpan.FromMilliseconds(Defaults.Retry.InitialDelayMilliseconds));
		}

		public static RetryPolicySettings Linear(int retryCount, TimeSpan initialDelay)
		{
			return new RetryPolicySettings(retryCount, SleepDurationProvider.Constant(retryCount, initialDelay));
		}

		public static RetryPolicySettings Exponential(int retryCount)
		{
			return Exponential(retryCount, TimeSpan.FromMilliseconds(Defaults.Retry.InitialDelayMilliseconds));
		}

		public static RetryPolicySettings Exponential(int retryCount, TimeSpan initialDelay)
		{
			return new RetryPolicySettings(retryCount, SleepDurationProvider.Exponential(retryCount, initialDelay));
		}

		public static RetryPolicySettings Jitter(int retryCount)
		{
			return Jitter(retryCount, TimeSpan.FromMilliseconds(Defaults.Retry.MedianFirstRetryDelayInMilliseconds));
		}

		public static RetryPolicySettings Jitter(int retryCount, TimeSpan medianFirstRetryDelay)
		{
			return new RetryPolicySettings(retryCount, SleepDurationProvider.Jitter(retryCount, medianFirstRetryDelay));
		}

		private TimeSpan? getServerWaitDuration(DelegateResult<HttpResponseMessage> response)
		{
			var retryAfter = response?.Result?.Headers?.RetryAfter;
			if (retryAfter == null)
			{
				return null;
			}

			if (retryAfter.Delta.HasValue) // Delta priority check, because its simple TimeSpan value
			{
				return retryAfter.Delta.Value;
			}

			if (retryAfter.Date.HasValue)
			{
				return retryAfter.Date.Value - DateTime.UtcNow;
			}

			return null; // when nothing was found
		}

		#region nested class

		private static class SleepDurationProvider
		{
			internal static Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> Constant(int retryCount, TimeSpan delay)
			{
				if (retryCount < 0) throw new ArgumentOutOfRangeException(nameof(retryCount), retryCount, "should be >= 0");
				if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), delay, "should be >= 0ms");

				return (i, r, c) => Backoff.ConstantBackoff(delay, retryCount).ToArray()[i - 1];
			}

			internal static Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> Linear(int retryCount, TimeSpan initialDelay)
			{
				if (retryCount < 0) throw new ArgumentOutOfRangeException(nameof(retryCount), retryCount, "should be >= 0");
				if (initialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initialDelay), initialDelay, "should be >= 0ms");

				return (i, r, c) => Backoff.LinearBackoff(initialDelay, retryCount).ToArray()[i - 1];
			}

			internal static Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> Exponential(int retryCount, TimeSpan initialDelay)
			{
				if (retryCount < 0) throw new ArgumentOutOfRangeException(nameof(retryCount), retryCount, "should be >= 0");
				if (initialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(initialDelay), initialDelay, "should be >= 0ms");

				return (i, r, c) => Backoff.ExponentialBackoff(initialDelay, retryCount).ToArray()[i - 1];
			}

			internal static Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> Jitter(int retryCount, TimeSpan medianFirstRetryDelay)
			{
				if (retryCount < 0) throw new ArgumentOutOfRangeException(nameof(retryCount), retryCount, "should be >= 0");
				if (medianFirstRetryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(medianFirstRetryDelay), medianFirstRetryDelay, "should be >= 0ms");

				return (i, r, c) => Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay, retryCount).ToArray()[i - 1];
			}

			#endregion
		}
	}
}
