using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;

namespace Dodo.HttpClientResiliencePolicies.RetryPolicy
{
	public partial class RetryPolicySettings : IRetryPolicySettings
	{
		private Action<DelegateResult<HttpResponseMessage>, TimeSpan> _onRetryHandler;

		public int RetryCount { get; }

		private readonly Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> _sleepDurationProvider;
		Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> IRetryPolicySettings.SleepDurationProvider =>
			(retryCount, response, context) =>
			{
				var serverWaitDuration = GetServerWaitDuration(response);
				return serverWaitDuration ?? _sleepDurationProvider(retryCount, response, context);
			};

		Action<DelegateResult<HttpResponseMessage>, TimeSpan> IRetryPolicySettings.OnRetry
		{
			get => _onRetryHandler;
			set => _onRetryHandler = value;
		}
		Func<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context, Task> IRetryPolicySettings.OnRetryWrapper =>
			(response, span, retryCount, context) =>
			{
				_onRetryHandler?.Invoke(response, span);
				return Task.CompletedTask;
			};

		public RetryPolicySettings()
		{
			_sleepDurationProvider = SleepDurationProvider.Jitter(
				Defaults.Retry.RetryCount,
				TimeSpan.FromMilliseconds(Defaults.Retry.MedianFirstRetryDelayInMilliseconds));

			_onRetryHandler = DoNothingOnRetry;
			RetryCount = Defaults.Retry.RetryCount;
		}

		private RetryPolicySettings(
			int retryCount,
			Func<int, DelegateResult<HttpResponseMessage>, Context, TimeSpan> sleepDurationProvider)
		{
			_sleepDurationProvider = sleepDurationProvider;
			_onRetryHandler = DoNothingOnRetry;
			RetryCount = retryCount;
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

		private static TimeSpan? GetServerWaitDuration(DelegateResult<HttpResponseMessage> response)
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
	}
}
