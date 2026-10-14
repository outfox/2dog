let now = (date now | format date "%Y %-m" | split row " ")
gh api $"organizations/outfox/settings/billing/usage/summary?year=($now.0)&month=($now.1)" 
	| from json
	| get usageItems
	| select sku grossQuantity unitType grossAmount netAmount
