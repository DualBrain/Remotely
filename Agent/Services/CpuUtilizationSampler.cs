﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Remotely.Agent.Services;

public interface ICpuUtilizationSampler : IHostedService
{
    double CurrentUtilization { get; }
}

internal class CpuUtilizationSampler : BackgroundService, ICpuUtilizationSampler
{
    private readonly ILogger<CpuUtilizationSampler> _logger;
    private double _currentUtilization;

    public CpuUtilizationSampler(ILogger<CpuUtilizationSampler> logger)
    {
        _logger = logger;
    }

    public double CurrentUtilization => _currentUtilization;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow host startup to continue immediately.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var currentUtil = await GetCpuUtilization(stoppingToken);
                Interlocked.Exchange(ref _currentUtilization, currentUtil);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while getting CPU utilization sample.");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private static async Task<double> GetCpuUtilization(CancellationToken cancelToken)
    {
        double totalUtilization = 0;
        var utilizations = new Dictionary<int, Tuple<DateTimeOffset, TimeSpan>>();
        var processes = Process.GetProcesses();

        foreach (var proc in processes)
        {
            if (cancelToken.IsCancellationRequested)
            {
                return 0;
            }

            try
            {
                var startTime = DateTimeOffset.Now;
                var startCpuUsage = proc.TotalProcessorTime;
                utilizations.Add(proc.Id, new Tuple<DateTimeOffset, TimeSpan>(startTime, startCpuUsage));
            }
            catch
            {
                continue;
            }
        }

        await Task.Delay(1_000, cancelToken);

        foreach (var kvp in utilizations)
        {
            if (cancelToken.IsCancellationRequested)
            {
                return 0;
            }

            var endTime = DateTimeOffset.Now;
            try
            {
                var proc = Process.GetProcessById(kvp.Key);
                var startTime = kvp.Value.Item1;
                var startCpuUsage = kvp.Value.Item2;
                var endCpuUsage = proc.TotalProcessorTime;
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                totalUtilization += cpuUsageTotal;
            }
            catch
            {
                continue;
            }
        }

        return totalUtilization;
    }
}