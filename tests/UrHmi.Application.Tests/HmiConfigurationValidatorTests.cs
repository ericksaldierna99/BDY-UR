using UrHmi.Application.Configuration;
using UrHmi.Domain.Configuration;

namespace UrHmi.Application.Tests;

public sealed class HmiConfigurationValidatorTests
{
    private readonly HmiConfigurationValidator _validator = new();

    [Fact]
    public void Default_configuration_is_valid()
    {
        var config = HmiConfiguration.CreateDefault();
        var errors = _validator.Validate(config);

        Assert.Empty(errors);
        Assert.Equal(HmiConfiguration.DefaultRobotIp, config.Robot.Ip);
    }

    [Fact]
    public void Invalid_ip_and_interval_are_rejected()
    {
        var config = HmiConfiguration.CreateDefault() with
        {
            Robot = HmiConfiguration.CreateDefault().Robot with
            {
                Ip = "not-an-ip",
                Dashboard = new DashboardChannelConfiguration(29999, 0)
            }
        };

        var errors = _validator.Validate(config);

        Assert.Contains(errors, e => e.Contains("robot.ip", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("staleTimeoutMs", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_plc_transport_is_rejected()
    {
        var config = HmiConfiguration.CreateDefault() with
        {
            Plc = HmiConfiguration.CreateDefault().Plc with { Transport = "profinet" }
        };

        var errors = _validator.Validate(config);

        Assert.Contains(errors, e => e.Contains("plc.transport", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_urscript_ports_are_rejected()
    {
        var config = HmiConfiguration.CreateDefault() with
        {
            Robot = HmiConfiguration.CreateDefault().Robot with
            {
                UrScript = new UrScriptChannelConfiguration(0, 70000)
            }
        };

        var errors = _validator.Validate(config);

        Assert.Contains(errors, e => e.Contains("robot.urscript.port", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("robot.urscript.fallbackPort", StringComparison.Ordinal));
    }
}
