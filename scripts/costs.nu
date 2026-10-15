# Requires authenticated GitHub (gh) and Blacksmith CLIs.
# Install Blacksmith: https://docs.blacksmith.sh/blacksmith-cli/overview
# On Windows, install it in the default WSL distro and run: wsl sh -lc "blacksmith auth login"

def --wrapped run-blacksmith [...args] {
    if $nu.os-info.name == "windows" {
        ^wsl --exec sh -lc 'exec blacksmith "$@"' blacksmith ...$args
    } else {
        ^blacksmith ...$args
    }
}

def cost-table [] {
    let rows = $in
    $env.config.float_precision = 2
    $rows | table --index false | print
}

def compute-breakdown [column: string, label: string] {
    let rows = $in
    if ($rows | is-empty) {
        print "No compute usage in this period."
        return
    }

    $rows
        | sort-by cost_usd --reverse
        | select $column jobs runtime_minutes cost_usd
        | update runtime_minutes { $in / 60 }
        | into float runtime_minutes cost_usd
        | rename $label Jobs 'Runtime (h)' 'Cost (USD)'
        | cost-table
}

let org = "outfox"
let now = (date now | date to-timezone UTC)
let year = ($now | format date "%Y")
let month = ($now | format date "%-m")
let start = ($now | format date "%Y-%m-01T00:00:00Z")
let end = ($now | format date "%Y-%m-%dT%H:%M:%SZ")

print $"GitHub: ($org), ($now | format date '%Y-%m')"
gh api $"organizations/($org)/settings/billing/usage/summary?year=($year)&month=($month)"
    | from json
    | get usageItems
    | select sku grossQuantity unitType grossAmount netAmount
    | print

let compute = (run-blacksmith usage --org $org --start-time $start --end-time $end --breakdown-by runner_type,repo --format json | from json)

print $"\nBlacksmith: ($org), ($now | format date '%Y-%m') — list-price estimates in USD"
$compute.summary
    | select jobs runtime_minutes cost_usd failed_cost_usd cancelled_cost_usd rerun_cost_usd
    | update runtime_minutes { $in / 60 }
    | into float runtime_minutes cost_usd failed_cost_usd cancelled_cost_usd rerun_cost_usd
    | rename Jobs 'Runtime (h)' 'Compute (USD)' 'Failed (USD)' 'Cancelled (USD)' 'Reruns (USD)'
    | transpose Metric Value
    | cost-table

print "\nCompute by repository"
$compute.breakdowns.repo | compute-breakdown repo Repository

print "\nCompute by runner"
$compute.breakdowns.runner_type | compute-breakdown runner_type Runner

let storage = (run-blacksmith usage storage --org $org --start-time $start --end-time $end --breakdown-by repo,type --format json | from json)

print "\nStorage"
if $storage.summary.billing_data_available {
    $storage.summary
        | select current_usage_gb peak_usage_gb avg_usage_gb gb_hours estimated_cost_usd
        | into float current_usage_gb peak_usage_gb avg_usage_gb gb_hours estimated_cost_usd
        | rename 'Current (GB)' 'Peak (GB)' 'Average (GB)' 'GB-hours' 'Cost (USD)'
        | transpose Metric Value
        | cost-table

    for group in [{column: repo, label: Repository}, {column: type, label: Type}] {
        let rows = ($storage.breakdowns | get $group.column)
        if not ($rows | is-empty) {
            print $"\nStorage by ($group.label | str downcase)"
            $rows
                | sort-by estimated_cost_usd --reverse
                | select $group.column gb_hours estimated_cost_usd
                | into float gb_hours estimated_cost_usd
                | rename $group.label 'GB-hours' 'Cost (USD)'
                | cost-table
        }
    }
} else {
    print "No storage billing data available for this period."
    if $storage.summary.current_usage_source != "none" {
        $storage.summary
            | select current_usage_gb
            | into float current_usage_gb
            | rename 'Current storage (GB)'
            | transpose Metric Value
            | cost-table
    }
}
