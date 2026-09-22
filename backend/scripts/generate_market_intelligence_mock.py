#!/usr/bin/env python3
"""Generate the demo market-intelligence corpus (backend/fixtures/market-intelligence.mock.json).

Deterministic (fixed seed). Keeps every hand-written legacy record untouched (their ids do not
start with the generated prefix) and appends a broad, plausible corpus built from public list
prices (2026, see PRICE_SOURCES) spread across regions, SKUs / editions, terms and company sizes.
Percentile bands are derived from the list price and the typical negotiated discount for that
category, size and term; every generated record is flagged source="mock" / representative=true
like the legacy ones (R-MKT-02).

Run:  python3 backend/scripts/generate_market_intelligence_mock.py
"""

from __future__ import annotations

import json
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FEED = ROOT / "backend" / "fixtures" / "market-intelligence.mock.json"
FEED_VERSION = "mock-2026.09.2"
# "ZZ" sorts after every hand-written id ("MKT-ZUR...", "MKT-ZOOM..."), so on a score tie the
# in-memory search and the benchmark adapter keep preferring the legacy record.
GENERATED_PREFIX = "MKT-ZZ-"
SEED = 20260921

# Public list prices used as anchors (USD unless the unit says otherwise). Kept as a comment
# trail for reviewers; the corpus stays "mock" — bands are modelled, not scraped deals.
PRICE_SOURCES = {
    "Salesforce Sales Cloud": "salesforce.com/sales/pricing (Starter 25, Pro Suite 100, Enterprise 175, Unlimited 350 USD/user/month)",
    "Microsoft 365": "microsoft.com plans-and-pricing (E3 39, E5 57, F3 10 USD/user/month from July 2026)",
    "ServiceNow ITSM": "industry estimates (Standard ~100, Professional 135-170, Enterprise 180-250 USD/fulfiller/month)",
    "Slack": "slack.com/pricing (Pro 7.25, Business+ 15, Enterprise Grid ~26 USD/user/month)",
    "Zoom Workplace": "zoom pricing (Pro 14.16, Business 18.33, Enterprise 19.99 USD/user/month)",
    "Atlassian": "atlassian.com pricing (Jira Standard 7.91, Premium 14.54; Confluence Standard 5.42, Premium 10.44 USD/user/month)",
    "Workday / SAP SuccessFactors / Oracle": "PEPM estimates (Workday 25-45, SuccessFactors 28-38 USD/employee/month; Oracle Fusion ERP 300-550 USD/user/month)",
    "Adobe / DocuSign / Okta": "Adobe CC teams 79.99 USD/seat/month; DocuSign Business Pro 40 USD/user/month; Okta SSO 2 + MFA 3 USD/user/month",
    "Cloud": "AWS m6i.large 0.096, Azure D4s v5 0.192, GCP n2-standard-4 0.194 USD/hour on-demand",
    "Google Workspace / Datadog / Zendesk": "Business Standard 14, Business Plus 22 USD/user/month; Datadog Infra Pro 15 USD/host/month; Zendesk Suite Professional 115 USD/agent/month",
    "Snowflake / Databricks / GitHub / HubSpot": "Snowflake 2/3/4 USD per credit; Databricks 0.15-0.65 USD/DBU; GitHub Enterprise 21 USD/user/month; HubSpot Sales Hub Pro 100, Enterprise 150 USD/seat/month",
    "Consulting": "Big Four day rates EUR 3000-5000 partner, 800-1800 manager, 400-1000 consultant; German market average ~EUR 1300/day",
    "Telco": "MPLS 100 Mbps EUR 700-1300/site/month, SD-WAN EUR 100-500/site/month (Western Europe)",
    "Insurance": "cyber EUR 25k-100k/year for EUR 5-20M limits (mid-market); property 3-8 USD per 1000 insured; fleet 1800-10800 USD/vehicle/year",
    "Logistics / Facilities": "EU pallet 56-78 EUR; Shanghai-Rotterdam 40ft ~4100 USD (Sep 2026); office cleaning 2.5-6 EUR/m2/month; corporate meal 12-25 USD",
}

# (region, currency, price multiplier vs USD list)
REGIONS = [
    ("US", "USD", 1.00),
    ("EU", "EUR", 0.95),
    ("UK", "GBP", 0.82),
    ("CH", "CHF", 0.92),
    ("APAC", "USD", 1.04),
]

SIZE_BANDS = ["50-500", "500-2000", "2000-5000", "5000-20000", "20000+"]

# Typical negotiated discount off list (median, in %) by category, then adjusted by size and term.
CATEGORY_DISCOUNT = {
    "Enterprise Software": 12.0,
    "Cloud Infrastructure": 18.0,
    "Professional Services": 8.0,
    "Telco": 15.0,
    "Insurance": 6.0,
    "Logistics": 7.0,
    "Facilities": 5.0,
}
SIZE_DISCOUNT_BONUS = {"50-500": -4.0, "500-2000": 0.0, "2000-5000": 3.0, "5000-20000": 6.0, "20000+": 9.0}
TERM_DISCOUNT_BONUS = {12: 0.0, 24: 3.0, 36: 6.0}

CLAUSE_POOL = {
    "Enterprise Software": [
        ("UpliftCap", "{cap}% annual cap"),
        ("PriceLock", "unit price locked for the term"),
        ("TrueDown", "seat reduction at each anniversary without penalty"),
        ("RenewalPriceProtection", "renewal at the same unit price"),
        ("MostFavouredCustomer", "list-price decreases passed through"),
        ("TerminationForConvenience", "exit at the midpoint with 90-day notice"),
    ],
    "Cloud Infrastructure": [
        ("CommitFlexibility", "committed spend portable across services"),
        ("UpliftCap", "{cap}% annual cap on committed rates"),
        ("EgressWaiver", "egress fees waived up to 10% of spend"),
        ("PriceLock", "committed rate locked for the term"),
    ],
    "Professional Services": [
        ("RateCardLock", "day rates fixed for the term"),
        ("SeniorityMix", "maximum share of senior roles per engagement"),
        ("NamedResources", "named resources, replacement subject to approval"),
        ("OutcomeFee", "share of the fee paid on milestone acceptance"),
    ],
    "Telco": [
        ("ReRate", "tariffs re-rated to market at each anniversary"),
        ("BenchmarkClause", "benchmark review on request"),
        ("SLACredits", "service credits for availability below target"),
        ("TerminationForConvenience", "exit at the midpoint with 90-day notice"),
    ],
    "Insurance": [
        ("PremiumCap", "renewal premium increase capped at {cap}%"),
        ("Remarketing", "remarketing right before renewal"),
        ("ClaimsReview", "quarterly claims review"),
    ],
    "Logistics": [
        ("VolumeTiers", "retroactive volume tiers"),
        ("SurchargeCap", "fuel surcharge capped at {cap}%"),
        ("RateReview", "semi-annual rate review"),
    ],
    "Facilities": [
        ("ScopeAudit", "annual scope audit with baseline credit"),
        ("IndexationCap", "indexation capped at {cap}%"),
        ("KPIRebate", "rebate on missed service KPIs"),
    ],
}

NOTICE_BY_CATEGORY = {
    "Enterprise Software": [30, 60, 90],
    "Cloud Infrastructure": [30, 60],
    "Professional Services": [30, 60, 90],
    "Telco": [90, 180],
    "Insurance": [60, 90],
    "Logistics": [60, 90],
    "Facilities": [90, 180],
}
PAYMENT_TERMS = ["Net 30", "Net 45", "Net 60", "Net 30", "Net 45"]
CLOSING_PERIODS = ["2025-Q3", "2025-Q4", "2026-Q1", "2026-Q2", "2026-Q3"]
UPDATED_AT_BY_PERIOD = {
    "2025-Q3": "2025-09-25T00:00:00Z",
    "2025-Q4": "2025-12-15T00:00:00Z",
    "2026-Q1": "2026-03-20T00:00:00Z",
    "2026-Q2": "2026-06-18T00:00:00Z",
    "2026-Q3": "2026-09-10T00:00:00Z",
}

# supplier, category, product, sku, list price (USD), unit metric label, annual value band hint,
# terms offered, typical quantity range for the annualValueBand computation.
# The catalog is deliberately wide: several suppliers per category, several editions per product.
CATALOG = [
    # --- CRM / sales ---
    ("Salesforce", "Enterprise Software", "Sales Cloud Starter", "SFDC-SALES-STARTER", 300, "per seat / year"),
    ("Salesforce", "Enterprise Software", "Sales Cloud Pro Suite", "SFDC-SALES-PRO", 1200, "per seat / year"),
    ("Salesforce", "Enterprise Software", "Sales Cloud Enterprise", "SFDC-SALES-ENT", 2100, "per seat / year"),
    ("Salesforce", "Enterprise Software", "Sales Cloud Unlimited", "SFDC-SALES-UNL", 4200, "per seat / year"),
    ("Salesforce", "Enterprise Software", "Service Cloud Enterprise", "SFDC-SERVICE-ENT", 2100, "per seat / year"),
    ("Salesforce", "Enterprise Software", "Marketing Cloud Engagement", "SFDC-MC-ENG", 15000, "per org / year"),
    ("HubSpot", "Enterprise Software", "Sales Hub Professional", "HS-SALES-PRO", 1200, "per seat / year"),
    ("HubSpot", "Enterprise Software", "Sales Hub Enterprise", "HS-SALES-ENT", 1800, "per seat / year"),
    ("HubSpot", "Enterprise Software", "Marketing Hub Professional", "HS-MKT-PRO", 10200, "per org / year"),
    ("Microsoft", "Enterprise Software", "Dynamics 365 Sales Enterprise", "MS-D365-SALES-ENT", 1140, "per seat / year"),
    # --- productivity / collaboration ---
    ("Microsoft", "Enterprise Software", "Microsoft 365 E3", "MS-M365-E3", 468, "per user / year"),
    ("Microsoft", "Enterprise Software", "Microsoft 365 E5", "MS-M365-E5", 684, "per user / year"),
    ("Microsoft", "Enterprise Software", "Microsoft 365 F3", "MS-M365-F3", 120, "per user / year"),
    ("Microsoft", "Enterprise Software", "Power BI Pro", "MS-PBI-PRO", 168, "per user / year"),
    ("Google Workspace", "Enterprise Software", "Business Standard", "GWS-BIZ-STD", 168, "per user / year"),
    ("Google Workspace", "Enterprise Software", "Business Plus", "GWS-BIZ-PLUS", 264, "per user / year"),
    ("Google Workspace", "Enterprise Software", "Enterprise Standard", "GWS-ENT-STD", 324, "per user / year"),
    ("Slack", "Enterprise Software", "Pro", "SLACK-PRO", 87, "per user / year"),
    ("Slack", "Enterprise Software", "Business+", "SLACK-BIZ-PLUS", 180, "per user / year"),
    ("Slack", "Enterprise Software", "Enterprise Grid", "SLACK-ENT-GRID", 310, "per user / year"),
    ("Zoom", "Enterprise Software", "Workplace Pro", "ZOOM-WP-PRO", 170, "per user / year"),
    ("Zoom", "Enterprise Software", "Workplace Business", "ZOOM-WP-BIZ", 220, "per user / year"),
    ("Zoom", "Enterprise Software", "Workplace Enterprise", "ZOOM-WP-ENT", 240, "per user / year"),
    ("Notion", "Enterprise Software", "Business Plan", "NOTION-BIZ", 240, "per user / year"),
    ("Notion", "Enterprise Software", "Enterprise Plan", "NOTION-ENT", 300, "per user / year"),
    ("Box", "Enterprise Software", "Business Plus", "BOX-BIZ-PLUS", 300, "per user / year"),
    ("Dropbox", "Enterprise Software", "Business", "DBX-BIZ", 216, "per user / year"),
    ("Atlassian", "Enterprise Software", "Jira Software Standard", "ATL-JIRA-STD", 95, "per user / year"),
    ("Atlassian", "Enterprise Software", "Jira Software Premium", "ATL-JIRA-PREM", 175, "per user / year"),
    ("Atlassian", "Enterprise Software", "Jira Software Enterprise", "ATL-JIRA-ENT", 220, "per user / year"),
    ("Atlassian", "Enterprise Software", "Confluence Standard", "ATL-CONF-STD", 65, "per user / year"),
    ("Atlassian", "Enterprise Software", "Confluence Premium", "ATL-CONF-PREM", 125, "per user / year"),
    ("Atlassian", "Enterprise Software", "Jira Service Management Premium", "ATL-JSM-PREM", 564, "per agent / year"),
    ("GitHub", "Enterprise Software", "Enterprise Cloud", "GH-ENT-CLOUD", 252, "per user / year"),
    ("Adobe", "Enterprise Software", "Creative Cloud Enterprise", "ADBE-CC-ENT", 1080, "per seat / year"),
    ("Adobe", "Enterprise Software", "Acrobat Pro", "ADBE-ACRO-PRO", 280, "per seat / year"),
    ("DocuSign", "Enterprise Software", "eSignature Business Pro", "DS-ESIG-BIZPRO", 480, "per user / year"),
    ("DocuSign", "Enterprise Software", "eSignature Enterprise Pro", "DS-ESIG-ENTPRO", 720, "per user / year"),
    ("Okta", "Enterprise Software", "Workforce Identity", "OKTA-WIC-SSO-MFA", 96, "per user / year"),
    ("Okta", "Enterprise Software", "Workforce Identity Adaptive", "OKTA-WIC-ADAPTIVE", 168, "per user / year"),
    # --- ITSM / support / observability ---
    ("ServiceNow", "Enterprise Software", "ITSM Standard", "SNOW-ITSM-STD", 1200, "per fulfiller / year"),
    ("ServiceNow", "Enterprise Software", "ITSM Professional", "SNOW-ITSM-PRO", 1800, "per fulfiller / year"),
    ("ServiceNow", "Enterprise Software", "ITSM Enterprise", "SNOW-ITSM-ENT", 2500, "per fulfiller / year"),
    ("ServiceNow", "Enterprise Software", "HR Service Delivery Professional", "SNOW-HRSD-PRO", 1500, "per fulfiller / year"),
    ("Zendesk", "Enterprise Software", "Suite Professional", "ZD-SUITE-PRO", 1380, "per agent / year"),
    ("Zendesk", "Enterprise Software", "Suite Enterprise", "ZD-SUITE-ENT", 1980, "per agent / year"),
    ("Datadog", "Enterprise Software", "Infrastructure Pro", "DDOG-INFRA-PRO", 180, "per host / year"),
    ("Datadog", "Enterprise Software", "APM", "DDOG-APM", 372, "per host / year"),
    # --- ERP / HCM ---
    ("Workday", "Enterprise Software", "HCM", "WDAY-HCM", 360, "per employee / year"),
    ("Workday", "Enterprise Software", "Financial Management", "WDAY-FIN", 300, "per employee / year"),
    ("SAP", "Enterprise Software", "SuccessFactors Employee Central", "SAP-SF-EC", 240, "per employee / year"),
    ("SAP", "Enterprise Software", "S/4HANA Cloud Professional Use", "SAP-S4-PRO", 2400, "per user / year"),
    ("SAP", "Enterprise Software", "Ariba Buying", "SAP-ARIBA-BUY", 90000, "per org / year"),
    ("Oracle", "Enterprise Software", "Fusion Cloud ERP Financials", "ORCL-FUSION-FIN", 5400, "per user / year"),
    ("Oracle", "Enterprise Software", "Fusion Cloud HCM", "ORCL-FUSION-HCM", 4200, "per user / year"),
    # --- data ---
    ("Snowflake", "Enterprise Software", "Standard Compute Credits", "SNOW-CR-STD", 2.0, "per credit"),
    ("Snowflake", "Enterprise Software", "Enterprise Compute Credits", "SNOW-CR-ENT", 3.0, "per credit"),
    ("Snowflake", "Enterprise Software", "Business Critical Compute Credits", "SNOW-CR-BC", 4.0, "per credit"),
    ("Databricks", "Enterprise Software", "Premium All-Purpose Compute", "DBX-DBU-PREM-AP", 0.55, "per DBU"),
    ("Databricks", "Enterprise Software", "Premium Jobs Compute", "DBX-DBU-PREM-JOBS", 0.30, "per DBU"),
    ("Tableau", "Enterprise Software", "Creator", "TAB-CREATOR", 900, "per user / year"),
    ("Tableau", "Enterprise Software", "Explorer", "TAB-EXPLORER", 504, "per user / year"),
    # --- cloud infrastructure ---
    ("AWS", "Cloud Infrastructure", "EC2 Compute", "m5.large", 0.096, "per instance-hour"),
    ("AWS", "Cloud Infrastructure", "EC2 Compute", "m6i.large", 0.096, "per instance-hour"),
    ("AWS", "Cloud Infrastructure", "EC2 Compute", "r6i.large", 0.126, "per instance-hour"),
    ("AWS", "Cloud Infrastructure", "EC2 Compute", "c6i.xlarge", 0.170, "per instance-hour"),
    ("AWS", "Cloud Infrastructure", "S3 Standard Storage", "S3-STD", 0.023, "per GB-month"),
    ("AWS", "Cloud Infrastructure", "RDS PostgreSQL", "db.m6i.large", 0.192, "per instance-hour"),
    ("Microsoft", "Cloud Infrastructure", "Azure Virtual Machines", "D4s_v5", 0.192, "per instance-hour"),
    ("Microsoft", "Cloud Infrastructure", "Azure Virtual Machines", "E4s_v5", 0.252, "per instance-hour"),
    ("Microsoft", "Cloud Infrastructure", "Azure Blob Storage Hot", "BLOB-HOT", 0.018, "per GB-month"),
    ("Google Cloud", "Cloud Infrastructure", "Compute Engine", "n2-standard-4", 0.194, "per instance-hour"),
    ("Google Cloud", "Cloud Infrastructure", "Compute Engine", "e2-standard-4", 0.134, "per instance-hour"),
    ("Google Cloud", "Cloud Infrastructure", "Cloud Storage Standard", "GCS-STD", 0.020, "per GB-month"),
    # --- professional services (EUR-anchored day rates expressed in USD list) ---
    ("Accenture", "Professional Services", "Managed IT Services", "ACN-MANAGED-IT", 1150, "per FTE-day"),
    ("Accenture", "Professional Services", "Technology Consulting", "ACN-TECH-CONS", 1600, "per consultant-day"),
    ("Deloitte", "Professional Services", "Advisory Services", "DTT-ADVISORY", 1900, "per consultant-day"),
    ("Deloitte", "Professional Services", "SAP Implementation", "DTT-SAP-IMPL", 1450, "per consultant-day"),
    ("PwC", "Professional Services", "Risk Advisory", "PWC-RISK", 1850, "per consultant-day"),
    ("EY", "Professional Services", "Transformation Consulting", "EY-TRANSFORM", 1800, "per consultant-day"),
    ("KPMG", "Professional Services", "Audit and Assurance", "KPMG-AUDIT", 1500, "per consultant-day"),
    ("Capgemini", "Professional Services", "Application Development", "CAP-APPDEV", 850, "per developer-day"),
    ("McKinsey", "Professional Services", "Strategy Consulting", "MCK-STRATEGY", 4800, "per partner-day"),
    ("Infosys", "Professional Services", "Application Maintenance", "INFY-AMS", 520, "per FTE-day"),
    ("TCS", "Professional Services", "Application Maintenance", "TCS-AMS", 500, "per FTE-day"),
    # --- telco ---
    ("Vodafone", "Telco", "Enterprise Mobile Plan", "VF-MOBILE-ENT", 336, "per SIM / year"),
    ("Vodafone", "Telco", "SD-WAN Site", "VF-SDWAN-SITE", 4800, "per site / year"),
    ("Deutsche Telekom", "Telco", "MPLS 100 Mbps Site", "DT-MPLS-100", 12000, "per site / year"),
    ("Deutsche Telekom", "Telco", "SD-WAN Enterprise", "DT-SDWAN-ENT", 5400, "per site / year"),
    ("Deutsche Telekom", "Telco", "Enterprise Mobile Plan", "DT-MOBILE-ENT", 360, "per SIM / year"),
    ("Swisscom", "Telco", "Enterprise Mobile Plan", "SC-MOBILE-ENT", 480, "per SIM / year"),
    ("Swisscom", "Telco", "Business Internet 1 Gbps", "SC-INET-1G", 10800, "per site / year"),
    ("Orange Business", "Telco", "SD-WAN Site", "OBS-SDWAN-SITE", 4500, "per site / year"),
    ("BT", "Telco", "MPLS 100 Mbps Site", "BT-MPLS-100", 13200, "per site / year"),
    ("Telefónica", "Telco", "Enterprise Mobile Plan", "TEF-MOBILE-ENT", 300, "per SIM / year"),
    # --- insurance ---
    ("Allianz", "Insurance", "Cyber Insurance", "ALZ-CYBER", 6000, "per EUR 1M limit / year"),
    ("Allianz", "Insurance", "Commercial Property", "ALZ-PROPERTY", 4.5, "per 1000 insured value / year"),
    ("Allianz", "Insurance", "Fleet Insurance", "ALZ-FLEET", 2400, "per vehicle / year"),
    ("AXA", "Insurance", "Cyber Insurance", "AXA-CYBER", 5800, "per EUR 1M limit / year"),
    ("AXA", "Insurance", "Directors and Officers", "AXA-DO", 9000, "per EUR 1M limit / year"),
    ("Zurich", "Insurance", "Commercial Property", "ZUR-PROPERTY", 4.8, "per 1000 insured value / year"),
    ("Zurich", "Insurance", "General Liability", "ZUR-GL", 1.8, "per 1000 turnover / year"),
    ("Generali", "Insurance", "Fleet Insurance", "GEN-FLEET", 2200, "per vehicle / year"),
    ("Generali", "Insurance", "Cyber Insurance", "GEN-CYBER", 6200, "per EUR 1M limit / year"),
    ("Chubb", "Insurance", "Directors and Officers", "CHUBB-DO", 9500, "per EUR 1M limit / year"),
    ("Swiss Re", "Insurance", "Reinsurance Treaty", "SR-TREATY", 0.9, "per 100 ceded premium"),
    # --- logistics ---
    ("DHL", "Logistics", "Road Freight EU Pallet", "DHL-ROAD-PALLET", 68, "per pallet"),
    ("DHL", "Logistics", "Domestic Parcel", "DHL-PARCEL-DOM", 5.9, "per parcel"),
    ("DHL", "Logistics", "Air Freight", "DHL-AIR-KG", 3.8, "per kg"),
    ("DB Schenker", "Logistics", "Road Freight Contract", "DBS-ROAD-CONTRACT", 66, "per pallet"),
    ("DB Schenker", "Logistics", "Contract Warehousing", "DBS-WAREHOUSE", 9.0, "per pallet-month"),
    ("Kuehne+Nagel", "Logistics", "Ocean FCL 40ft Asia-Europe", "KN-FCL40-ASIA-EU", 4100, "per container"),
    ("Kuehne+Nagel", "Logistics", "Air Freight", "KN-AIR-KG", 3.6, "per kg"),
    ("DSV", "Logistics", "Road Freight EU Pallet", "DSV-ROAD-PALLET", 64, "per pallet"),
    ("Maersk", "Logistics", "Ocean FCL 40ft Asia-Europe", "MAERSK-FCL40", 3950, "per container"),
    ("UPS", "Logistics", "Domestic Parcel", "UPS-PARCEL-DOM", 6.4, "per parcel"),
    ("FedEx", "Logistics", "International Express", "FDX-INTL-EXP", 42, "per shipment"),
    # --- facilities ---
    ("ISS Facility Services", "Facilities", "Office Cleaning", "ISS-CLEANING", 45.6, "per m2 / year"),
    ("ISS Facility Services", "Facilities", "Reception and Security", "ISS-SECURITY", 32, "per guard-hour"),
    ("Sodexo", "Facilities", "Corporate Catering", "SDX-CATERING", 11.0, "per meal"),
    ("Sodexo", "Facilities", "Office Cleaning", "SDX-CLEANING", 44.0, "per m2 / year"),
    ("CBRE", "Facilities", "Workplace Management", "CBRE-WPM", 18.0, "per m2 / year"),
    ("JLL", "Facilities", "Integrated Facility Management", "JLL-IFM", 21.0, "per m2 / year"),
    ("Compass Group", "Facilities", "Corporate Catering", "CG-CATERING", 10.5, "per meal"),
    ("Aramark", "Facilities", "Corporate Catering", "ARA-CATERING", 10.8, "per meal"),
]

# Which regions each category is generated for (some suppliers are regional by nature).
REGIONAL_SUPPLIERS = {
    "Swisscom": ["CH"],
    "Deutsche Telekom": ["EU", "CH"],
    "Vodafone": ["EU", "UK"],
    "BT": ["UK", "EU"],
    "Orange Business": ["EU"],
    "Telefónica": ["EU"],
    "Generali": ["EU", "CH"],
    "Allianz": ["EU", "CH", "UK"],
    "AXA": ["EU", "CH", "UK"],
    "Zurich": ["CH", "EU", "US"],
    "Chubb": ["US", "UK", "EU"],
    "Swiss Re": ["CH", "EU"],
    "DB Schenker": ["EU", "CH"],
    "DSV": ["EU", "UK"],
    "ISS Facility Services": ["EU", "UK", "CH"],
    "Sodexo": ["EU", "UK", "US"],
    "CBRE": ["US", "UK", "EU"],
    "JLL": ["US", "UK", "EU", "APAC"],
    "Compass Group": ["UK", "EU", "US"],
    "Aramark": ["US"],
    "Infosys": ["US", "EU", "UK", "APAC"],
    "TCS": ["US", "EU", "UK", "APAC"],
    "McKinsey": ["US", "EU", "UK", "CH"],
    "KPMG": ["EU", "UK", "CH", "US"],
    "EY": ["EU", "UK", "CH", "US"],
    "PwC": ["EU", "UK", "CH", "US"],
    "Deloitte": ["EU", "UK", "CH", "US"],
    "Accenture": ["US", "EU", "UK", "CH"],
    "Capgemini": ["EU", "UK"],
}

TERMS_BY_CATEGORY = {
    "Enterprise Software": [12, 36],
    "Cloud Infrastructure": [12, 36],
    "Professional Services": [12, 24],
    "Telco": [24, 36],
    "Insurance": [12],
    "Logistics": [12, 24],
    "Facilities": [36],
}


def r2(x: float) -> float:
    return float(f"{x:.2f}") if x < 100 else float(round(x))


def annual_value_band(list_price_usd: float, unit: str, size: str, rng: random.Random) -> str:
    # Rough annual value of a typical deal for this unit and company size.
    per_head = "seat" in unit or "user" in unit or "employee" in unit or "fulfiller" in unit or "agent" in unit or "SIM" in unit
    heads = {"50-500": 250, "500-2000": 1000, "2000-5000": 3000, "5000-20000": 9000, "20000+": 30000}[size]
    if per_head:
        value = list_price_usd * heads * rng.uniform(0.2, 0.6)
    elif "org" in unit or "limit" in unit or "site" in unit or "container" in unit or "vehicle" in unit:
        value = list_price_usd * rng.uniform(3, 40)
    else:
        value = rng.uniform(60_000, 3_000_000)
    if value < 100_000:
        return "<100k"
    if value < 250_000:
        return "100k-250k"
    if value < 500_000:
        return "250k-500k"
    if value < 1_000_000:
        return "500k-1m"
    if value < 5_000_000:
        return "1m-5m"
    return "5m+"


def generate() -> list[dict]:
    rng = random.Random(SEED)
    rows: list[dict] = []
    counter = 0
    for supplier, category, product, sku, list_usd, unit in CATALOG:
        regions = [r for r in REGIONS if r[0] in REGIONAL_SUPPLIERS.get(supplier, ["US", "EU", "UK", "CH", "APAC"])]
        terms = TERMS_BY_CATEGORY[category]
        for region, currency, fx in regions:
            for term in terms:
                # Two or three size bands per (product, region, term) keep the corpus dense but not exhaustive.
                sizes = rng.sample(SIZE_BANDS, k=rng.choice([1, 2, 2]))
                for size in sorted(sizes, key=SIZE_BANDS.index):
                    counter += 1
                    base_discount = CATEGORY_DISCOUNT[category] + SIZE_DISCOUNT_BONUS[size] + TERM_DISCOUNT_BONUS[term]
                    discount = max(2.0, base_discount + rng.uniform(-2.5, 2.5))
                    list_local = list_usd * fx
                    p50 = list_local * (1 - discount / 100)
                    spread = rng.uniform(0.08, 0.14)
                    p25 = p50 * (1 - spread)
                    p75 = p50 * (1 + spread * rng.uniform(0.9, 1.3))
                    sample = rng.choice([6, 9, 14, 22, 35, 48, 70, 110, 180, 260, 400])
                    if size in ("5000-20000", "20000+"):
                        sample = max(6, sample // 2)
                    period = rng.choice(CLOSING_PERIODS)
                    cap = rng.choice([3, 4, 5, 5, 6, 7])
                    clause_pool = CLAUSE_POOL[category]
                    clauses = [
                        {"type": t, "value": v.format(cap=cap)}
                        for t, v in rng.sample(clause_pool, k=rng.choice([0, 1, 2, 2, 3]))
                    ]
                    has_uplift = any(c["type"] in ("UpliftCap", "PremiumCap", "IndexationCap", "SurchargeCap") for c in clauses)
                    rows.append(
                        {
                            "provider": "Internal Dataset",
                            "recordId": f"{GENERATED_PREFIX}{counter:04d}",
                            "supplier": supplier,
                            "category": category,
                            "product": product,
                            "sku": sku,
                            "geography": region,
                            "currency": currency,
                            "companySizeBand": size,
                            "termMonths": term,
                            "annualValueBand": annual_value_band(list_usd, unit, size, rng),
                            "unitPriceP25": r2(p25),
                            "unitPriceP50": r2(p50),
                            "unitPriceP75": r2(p75),
                            "discountAchievedPct": round(discount, 1),
                            "upliftCapPct": cap if has_uplift else (rng.choice([None, None, cap]) if category == "Enterprise Software" else None),
                            "noticeDays": rng.choice(NOTICE_BY_CATEGORY[category]),
                            "paymentTerms": rng.choice(PAYMENT_TERMS),
                            "negotiatedClauses": clauses,
                            "closingPeriod": period,
                            "sampleSize": sample,
                            "source": "mock",
                            "representative": True,
                            "updatedAt": UPDATED_AT_BY_PERIOD[period],
                            "licenseRestrictions": None,
                            "unitMetric": unit,
                        }
                    )
    return rows


def main() -> None:
    feed = json.loads(FEED.read_text(encoding="utf-8"))
    legacy = [d for d in feed["deals"] if not d["recordId"].startswith(GENERATED_PREFIX)]
    legacy_products = {(d["supplier"].lower(), d["product"].lower()) for d in legacy}

    generated = [
        d for d in generate()
        # A hand-written (supplier, product) pair is an oracle for tests and golden cases (several
        # are deliberately thin so the "insufficient market data" path stays reachable): generated
        # rows never add to those pairs, they widen the corpus around them.
        if (d["supplier"].lower(), d["product"].lower()) not in legacy_products
    ]
    for d in generated:
        d.pop("unitMetric", None)

    feed["feedVersion"] = FEED_VERSION
    feed["deals"] = legacy + generated
    FEED.write_text(json.dumps(feed, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"legacy={len(legacy)} generated={len(generated)} total={len(feed['deals'])} -> {FEED}")


if __name__ == "__main__":
    main()
