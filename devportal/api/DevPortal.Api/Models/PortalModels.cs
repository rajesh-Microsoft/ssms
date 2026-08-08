namespace DevPortal.Api.Models;

/// <summary>How to reach a box to read its deployed state. "none" means we cannot,
/// and the portal will say so rather than guess.</summary>
public class ProbeConfig
{
    public string Type { get; set; } = "none";      // none | ssh | az

    /// <summary>Directory holding DEPLOYED_COMMIT / DEPLOYED_VERIFIED.</summary>
    public string? Root { get; set; }

    /// <summary>Docker Compose project name. Used to select containers by label,
    /// because dev and UAT share a host and their names overlap.</summary>
    public string? ComposeProject { get; set; }

    // ssh
    public string? Host { get; set; }
    public string? User { get; set; }
    public string? KeyPath { get; set; }

    // az vm run-command
    public string? Subscription { get; set; }
    public string? ResourceGroup { get; set; }
    public string? VmName { get; set; }
}

public class EnvironmentConfig
{
    public string Id { get; set; } = "";
    public string App { get; set; } = "";
    public string Tier { get; set; } = "DEV";       // DEV | PREPROD | UAT | PROD
    public string Name { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string? AppUrl { get; set; }
    public string? ApiUrl { get; set; }
    public string DataSensitivity { get; set; } = "";
    public bool DeploymentEnabled { get; set; }
    public bool Hazard { get; set; }
    public string? Note { get; set; }
    public string? Image { get; set; }
    public string? Pipeline { get; set; }
    public string? Database { get; set; }
    public string? Server { get; set; }
    public ProbeConfig Probe { get; set; } = new();
}

public class PortalOptions
{
    public List<EnvironmentConfig> Environments { get; set; } = new();
    public List<ApplicationConfig> Applications { get; set; } = new();

    /// <summary>Seconds a probe result is reused. Probing shells out, so this keeps
    /// a dashboard refresh from opening an SSH session per card.</summary>
    public int CacheSeconds { get; set; } = 30;
    public int ProbeTimeoutSeconds { get; set; } = 25;
}

public class ApplicationConfig
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "bi-box";
    public string Description { get; set; } = "";
    public bool Active { get; set; }
}

public record ContainerStatus(string Name, string Status);

/// <summary>What the portal renders. Anything the probe could not establish stays null
/// and <see cref="ProbeState"/> explains why — never a stand-in value.</summary>
public record EnvironmentStatus
{
    public string Id { get; init; } = "";
    public string App { get; init; } = "";
    public string Tier { get; init; } = "";
    public string Name { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string? AppUrl { get; init; }
    public string? ApiUrl { get; init; }
    public string DataSensitivity { get; init; } = "";
    public bool DeploymentEnabled { get; init; }
    public bool Hazard { get; init; }
    public string? Note { get; init; }
    public string? Image { get; init; }
    public string? Pipeline { get; init; }
    public string? Database { get; init; }
    public string? Server { get; init; }

    public string? Commit { get; init; }
    public string? VerifiedCommit { get; init; }
    public bool SignedOff => Commit is not null && Commit == VerifiedCommit;
    public IReadOnlyList<ContainerStatus> Containers { get; init; } = Array.Empty<ContainerStatus>();

    public int? SiteHttpCode { get; init; }
    public int? ApiHttpCode { get; init; }
    public string Health { get; init; } = "Unknown";

    /// <summary>ok | unavailable | disabled</summary>
    public string ProbeState { get; init; } = "unavailable";
    public string? ProbeError { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
