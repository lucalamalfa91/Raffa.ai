#!/usr/bin/env python3
"""Generate the mock market-benchmark corpus and its benchmark reports.

TEST DATA ONLY. Every record is synthetic: it is modelled from approximate public list prices and
typical negotiated discounts, not taken from real deals, and is flagged source="mock" /
representative=true (R-MKT-02). It exists so matching, estimates and savings have a dense,
Vendr/Tropic-shaped dataset to work against in development and demos.

Writes:
  backend/fixtures/market-intelligence.mock.json  hand-written records (ids not starting with
                                                  GENERATED_PREFIX) kept byte-identical; every
                                                  generated record replaced by this run's.
  backend/fixtures/market-benchmarks/*.md          benchmark reports by country, company size and
                                                  product category, aggregated from the corpus.

Deterministic (fixed seed). Run:  python3 backend/scripts/generate_market_benchmark_corpus.py
"""

from __future__ import annotations

import json
import random
import statistics
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FEED = ROOT / "backend" / "fixtures" / "market-intelligence.mock.json"
REPORTS = ROOT / "backend" / "fixtures" / "market-benchmarks"
FEED_VERSION = "mock-2026.09.3"
# Sorts after every hand-written id ("MKT-ZUR...", "MKT-ZOOM..."), so on a tie the hand-written
# oracle records keep winning.
GENERATED_PREFIX = "MKT-ZZ-"
SEED = 20260924

# --------------------------------------------------------------------------------------------
# Geography: (country, region, currency, software price-book factor vs USD list, labour index)
# --------------------------------------------------------------------------------------------
COUNTRIES = [
    ("US", "NA", "USD", 1.00, 1.00),
    ("CA", "NA", "USD", 0.97, 0.88),
    ("UK", "UK", "GBP", 0.80, 0.95),
    ("IE", "EU", "EUR", 0.93, 0.95),
    ("DE", "EU", "EUR", 0.93, 1.00),
    ("FR", "EU", "EUR", 0.93, 0.97),
    ("IT", "EU", "EUR", 0.91, 0.82),
    ("ES", "EU", "EUR", 0.90, 0.75),
    ("NL", "EU", "EUR", 0.93, 1.00),
    ("BE", "EU", "EUR", 0.93, 0.98),
    ("AT", "EU", "EUR", 0.93, 0.95),
    ("SE", "EU", "EUR", 0.95, 1.02),
    ("DK", "EU", "EUR", 0.96, 1.08),
    ("PL", "EU", "EUR", 0.86, 0.52),
    ("CH", "CH", "CHF", 0.90, 1.35),
    ("AU", "APAC", "USD", 1.05, 0.98),
    ("SG", "APAC", "USD", 1.03, 0.92),
    ("JP", "APAC", "USD", 1.06, 0.90),
    ("IN", "APAC", "USD", 0.85, 0.30),
    ("BR", "LATAM", "USD", 1.02, 0.50),
]
ALL_REGIONS = ["NA", "UK", "EU", "CH", "APAC", "LATAM"]
LABOUR_CATEGORIES = {"Professional Services", "Facilities", "Logistics"}

SIZE_BANDS = ["50-500", "500-2000", "2000-5000", "5000-20000", "20000+"]
SIZE_HEADS = {"50-500": 250, "500-2000": 1000, "2000-5000": 3000, "5000-20000": 9000, "20000+": 30000}
SIZE_FEE_SCALE = {"50-500": 1.0, "500-2000": 2.0, "2000-5000": 4.0, "5000-20000": 8.0, "20000+": 14.0}

INDUSTRIES = [
    "Financial Services", "Manufacturing", "Retail", "Healthcare", "Technology", "Public Sector",
    "Energy & Utilities", "Professional Services", "Media & Telecom", "Transport & Logistics",
]

# Median negotiated discount off list (%), then adjusted by size and term.
CATEGORY_DISCOUNT = {
    "Enterprise Software": 14.0, "Security Software": 16.0, "Cloud Infrastructure": 18.0,
    "Support & Services": 10.0, "Professional Services": 8.0, "Telco": 15.0, "Insurance": 6.0,
    "Logistics": 7.0, "Facilities": 5.0,
}
# Seat-priced software buys far below list at scale; services and physical goods much less so.
SIZE_DISCOUNT = {
    "software": {"50-500": 0.0, "500-2000": 8.0, "2000-5000": 16.0, "5000-20000": 24.0, "20000+": 30.0},
    "other": {"50-500": -3.0, "500-2000": 0.0, "2000-5000": 2.5, "5000-20000": 5.0, "20000+": 7.5},
}
TERM_DISCOUNT = {12: 0.0, 24: 3.0, 36: 6.0}
TERMS = {
    "Enterprise Software": [12, 36], "Security Software": [12, 36], "Cloud Infrastructure": [12, 36],
    "Support & Services": [12, 36], "Professional Services": [12, 24], "Telco": [24, 36],
    "Insurance": [12], "Logistics": [12, 24], "Facilities": [36],
}

# --------------------------------------------------------------------------------------------
# Catalog: (supplier, category, product, sku, price, unit[, regions])
# price: a USD list price per unit, or
#        ("pct", share, per-seat list)  a plan priced as a share of the licences it covers,
#        ("fee", base)                  a one-off fee that grows with the organisation.
# --------------------------------------------------------------------------------------------
Y = "per user / year"
CATALOG = [
    # Core suites (the products most contracts carry)
    ("Salesforce", "Enterprise Software", "Sales Cloud Starter", "SFDC-SALES-STARTER", 300, Y),
    ("Salesforce", "Enterprise Software", "Sales Cloud Pro Suite", "SFDC-SALES-PRO", 1200, Y),
    ("Salesforce", "Enterprise Software", "Sales Cloud Enterprise", "SFDC-SALES-ENT", 2100, Y),
    ("Salesforce", "Enterprise Software", "Sales Cloud Unlimited", "SFDC-SALES-UNL", 4200, Y),
    ("Salesforce", "Enterprise Software", "Service Cloud Enterprise", "SFDC-SVC-ENT", 2100, Y),
    ("HubSpot", "Enterprise Software", "Sales Hub Professional", "HS-SALES-PRO", 1200, Y),
    ("HubSpot", "Enterprise Software", "Sales Hub Enterprise", "HS-SALES-ENT", 1800, Y),
    ("Microsoft", "Enterprise Software", "Microsoft 365 E3", "MS-M365-E3", 468, Y),
    ("Microsoft", "Enterprise Software", "Microsoft 365 E5", "MS-M365-E5", 684, Y),
    ("Microsoft", "Enterprise Software", "Microsoft 365 F3", "MS-M365-F3", 120, Y),
    ("Microsoft", "Enterprise Software", "Power BI Pro", "MS-PBI-PRO", 168, Y),
    ("Microsoft", "Enterprise Software", "Dynamics 365 Sales Enterprise", "MS-D365-SALES", 1260, Y),
    ("Google", "Enterprise Software", "Google Workspace Business Standard", "GWS-BIZ-STD", 168, Y),
    ("Google", "Enterprise Software", "Google Workspace Enterprise Standard", "GWS-ENT-STD", 324, Y),
    ("Slack", "Enterprise Software", "Slack Business+", "SLACK-BIZPLUS", 180, Y),
    ("Zoom", "Enterprise Software", "Zoom Workplace Business", "ZOOM-WP-BIZ", 220, Y),
    ("Atlassian", "Enterprise Software", "Jira Software Standard", "ATL-JIRA-STD", 95, Y),
    ("Atlassian", "Enterprise Software", "Jira Software Premium", "ATL-JIRA-PREM", 175, Y),
    ("Atlassian", "Enterprise Software", "Confluence Standard", "ATL-CONF-STD", 65, Y),
    ("Atlassian", "Enterprise Software", "Confluence Premium", "ATL-CONF-PREM", 125, Y),
    ("Atlassian", "Enterprise Software", "Jira Service Management Premium", "ATL-JSM-PREM", 564, "per agent / year"),
    ("GitHub", "Enterprise Software", "GitHub Enterprise Cloud", "GH-ENT", 252, Y),
    ("Adobe", "Enterprise Software", "Creative Cloud All Apps Enterprise", "ADBE-CC-ENT", 1080, Y),
    ("Adobe", "Enterprise Software", "Acrobat Pro", "ADBE-ACRO-PRO", 280, Y),
    ("DocuSign", "Enterprise Software", "eSignature Business Pro", "DS-ESIG-BP", 480, Y),
    ("Okta", "Security Software", "Okta Workforce Identity SSO + MFA", "OKTA-WIC", 96, Y),
    ("ServiceNow", "Enterprise Software", "ITSM Professional", "SNOW-ITSM-PRO", 1800, "per fulfiller / year"),
    ("ServiceNow", "Enterprise Software", "ITSM Enterprise", "SNOW-ITSM-ENT", 2500, "per fulfiller / year"),
    ("Zendesk", "Enterprise Software", "Zendesk Suite Professional", "ZD-SUITE-PRO", 1380, "per agent / year"),
    ("Datadog", "Enterprise Software", "Datadog Infrastructure Pro", "DDOG-INFRA-PRO", 180, "per host / year"),
    ("Workday", "Enterprise Software", "Workday HCM", "WDAY-HCM", 360, "per employee / year"),
    ("SAP", "Enterprise Software", "SuccessFactors Employee Central", "SAP-SF-EC", 240, "per employee / year"),
    ("SAP", "Enterprise Software", "S/4HANA Cloud Professional Use", "SAP-S4-PRO", 2400, Y),
    ("Oracle", "Enterprise Software", "Fusion Cloud ERP Financials", "ORCL-FUSION-FIN", 5400, Y),
    ("Snowflake", "Enterprise Software", "Snowflake Enterprise Credits", "SNOW-CR-ENT", 3.0, "per credit"),
    ("Databricks", "Enterprise Software", "Databricks Premium Jobs Compute", "DBX-JOBS", 0.30, "per DBU"),
    ("Tableau", "Enterprise Software", "Tableau Creator", "TAB-CREATOR", 900, Y),
    ("AWS", "Cloud Infrastructure", "EC2 m6i.large", "m6i.large", 0.096, "per instance-hour"),
    ("Microsoft", "Cloud Infrastructure", "Azure D4s v5", "D4s_v5", 0.192, "per instance-hour"),
    ("Google Cloud", "Cloud Infrastructure", "Compute Engine n2-standard-4", "n2-standard-4", 0.194, "per instance-hour"),
    ("Deloitte", "Professional Services", "SAP Implementation", "DTT-SAP-IMPL", 1450, "per consultant-day"),
    ("Capgemini", "Professional Services", "Application Development", "CAP-APPDEV", 850, "per developer-day"),
    ("Vodafone", "Telco", "Enterprise Mobile Plan", "VF-MOBILE", 336, "per SIM / year", ["EU", "UK"]),
    ("Allianz", "Insurance", "Cyber Insurance", "ALZ-CYBER", 6000, "per 1M limit / year", ["EU", "CH", "UK"]),
    ("DHL", "Logistics", "Road Freight EU Pallet", "DHL-PALLET", 68, "per pallet", ["EU", "CH", "UK"]),
    ("Sodexo", "Facilities", "Corporate Catering", "SDX-CATERING", 11.0, "per meal"),
    # Content & collaboration
    ("Box", "Enterprise Software", "Box Business", "BOX-BIZ", 180, Y),
    ("Box", "Enterprise Software", "Box Business Plus", "BOX-BIZPLUS", 300, Y),
    ("Box", "Enterprise Software", "Box Enterprise", "BOX-ENT", 420, Y),
    ("Box", "Enterprise Software", "Box Enterprise Plus", "BOX-ENTPLUS", 600, Y),
    ("Box", "Enterprise Software", "Box Enterprise Advanced", "BOX-ENTADV", 780, Y),
    ("Box", "Support & Services", "Box Premium Support", "BOX-PREM-SUP", ("pct", 0.20, 600), "per org / year"),
    ("Box", "Support & Services", "Box Consulting Onboarding", "BOX-ONBOARD", ("fee", 12000), "one-off per org"),
    ("Dropbox", "Enterprise Software", "Dropbox Business", "DBX-BUS", 180, Y),
    ("Dropbox", "Enterprise Software", "Dropbox Business Plus", "DBX-BUSPLUS", 312, Y),
    ("Dropbox", "Enterprise Software", "Dropbox Enterprise", "DBX-ENT", 420, Y),
    ("Egnyte", "Enterprise Software", "Egnyte Business", "EGN-BUS", 240, Y),
    ("Egnyte", "Enterprise Software", "Egnyte Enterprise Lite", "EGN-ENTL", 480, Y),
    ("Microsoft", "Enterprise Software", "Microsoft 365 Business Premium", "MS-M365-BP", 264, Y),
    ("Microsoft", "Enterprise Software", "Microsoft 365 E3 (no Teams)", "MS-M365-E3NT", 407, Y),
    ("Microsoft", "Enterprise Software", "Microsoft 365 E5 (no Teams)", "MS-M365-E5NT", 655, Y),
    ("Microsoft", "Enterprise Software", "Microsoft Teams Enterprise", "MS-TEAMS-ENT", 63, Y),
    ("Microsoft", "Enterprise Software", "Microsoft 365 Copilot", "MS-COPILOT", 360, Y),
    ("Microsoft", "Enterprise Software", "Visio Plan 2", "MS-VISIO-P2", 180, Y),
    ("Microsoft", "Enterprise Software", "Project Plan 3", "MS-PROJ-P3", 360, Y),
    ("Microsoft", "Support & Services", "Microsoft Unified Support Enterprise", "MS-UNIFIED", ("pct", 0.09, 450), "per org / year"),
    ("Google", "Enterprise Software", "Google Workspace Enterprise Plus", "GWS-ENTPLUS", 420, Y),
    ("Google", "Enterprise Software", "Gemini for Workspace Enterprise", "GWS-GEMINI", 360, Y),
    ("Miro", "Enterprise Software", "Miro Business", "MIRO-BUS", 192, Y),
    ("Miro", "Enterprise Software", "Miro Enterprise", "MIRO-ENT", 300, Y),
    ("Lucid", "Enterprise Software", "Lucid Suite Enterprise", "LUCID-ENT", 240, Y),
    ("Asana", "Enterprise Software", "Asana Business", "ASANA-BUS", 300, Y),
    ("Asana", "Enterprise Software", "Asana Enterprise", "ASANA-ENT", 420, Y),
    ("monday.com", "Enterprise Software", "monday Work Management Pro", "MONDAY-PRO", 228, Y),
    ("monday.com", "Enterprise Software", "monday Work Management Enterprise", "MONDAY-ENT", 360, Y),
    ("Smartsheet", "Enterprise Software", "Smartsheet Business", "SMSH-BUS", 288, Y),
    ("Smartsheet", "Enterprise Software", "Smartsheet Enterprise", "SMSH-ENT", 420, Y),
    ("Figma", "Enterprise Software", "Figma Organization", "FIGMA-ORG", 660, Y),
    ("Figma", "Enterprise Software", "Figma Enterprise", "FIGMA-ENT", 1080, Y),
    ("Canva", "Enterprise Software", "Canva Enterprise", "CANVA-ENT", 360, Y),
    ("Cisco", "Enterprise Software", "Webex Suite Enterprise", "CSCO-WEBEX", 300, Y),
    ("RingCentral", "Enterprise Software", "RingEX Premium", "RC-RINGEX-PREM", 360, Y),
    ("RingCentral", "Enterprise Software", "RingEX Ultimate", "RC-RINGEX-ULT", 420, Y),
    ("Zoom", "Enterprise Software", "Zoom Phone Global Select", "ZOOM-PHONE-GS", 240, Y),
    ("Zoom", "Enterprise Software", "Zoom Rooms", "ZOOM-ROOMS", 588, "per room / year"),
    # Developer & data
    ("GitHub", "Enterprise Software", "GitHub Team", "GH-TEAM", 48, Y),
    ("GitHub", "Enterprise Software", "GitHub Copilot Business", "GH-COPILOT-BUS", 228, Y),
    ("GitHub", "Enterprise Software", "GitHub Copilot Enterprise", "GH-COPILOT-ENT", 468, Y),
    ("GitHub", "Enterprise Software", "GitHub Advanced Security", "GH-GHAS", 588, "per committer / year"),
    ("GitLab", "Enterprise Software", "GitLab Premium", "GL-PREM", 348, Y),
    ("GitLab", "Enterprise Software", "GitLab Ultimate", "GL-ULT", 1188, Y),
    ("JetBrains", "Enterprise Software", "All Products Pack (organization)", "JB-ALL-ORG", 779, Y),
    ("Postman", "Enterprise Software", "Postman Enterprise", "POSTMAN-ENT", 588, Y),
    ("OpenAI", "Enterprise Software", "ChatGPT Enterprise", "OAI-CHATGPT-ENT", 720, Y),
    ("OpenAI", "Enterprise Software", "ChatGPT Team", "OAI-CHATGPT-TEAM", 300, Y),
    ("Anthropic", "Enterprise Software", "Claude Team", "ANT-CLAUDE-TEAM", 300, Y),
    ("Anthropic", "Enterprise Software", "Claude Enterprise", "ANT-CLAUDE-ENT", 720, Y),
    ("Splunk", "Enterprise Software", "Splunk Cloud Ingest", "SPLK-CLOUD-GB", 1800, "per GB-day / year"),
    ("Elastic", "Enterprise Software", "Elastic Cloud Enterprise", "ELASTIC-ENT", 16000, "per org / year"),
    ("New Relic", "Enterprise Software", "New Relic Full Platform User", "NR-FULL", 5268, Y),
    ("Dynatrace", "Enterprise Software", "Dynatrace Full-Stack Monitoring", "DT-FULLSTACK", 690, "per host / year"),
    ("PagerDuty", "Enterprise Software", "PagerDuty Business", "PD-BUS", 492, Y),
    ("Grafana Labs", "Enterprise Software", "Grafana Cloud Enterprise", "GRAF-ENT", 25000, "per org / year"),
    ("MongoDB", "Enterprise Software", "Atlas Dedicated M30", "MDB-ATLAS-M30", 4700, "per cluster / year"),
    ("Confluent", "Enterprise Software", "Confluent Cloud Standard", "CFLT-STD", 12000, "per cluster / year"),
    ("Fivetran", "Enterprise Software", "Fivetran Standard", "FIVETRAN-STD", 30000, "per org / year"),
    ("dbt Labs", "Enterprise Software", "dbt Cloud Enterprise", "DBT-ENT", 1200, Y),
    ("Looker", "Enterprise Software", "Looker Standard Viewer", "LOOKER-VIEW", 360, Y),
    ("Qlik", "Enterprise Software", "Qlik Cloud Analytics Premium", "QLIK-PREM", 55000, "per org / year"),
    ("Alteryx", "Enterprise Software", "Alteryx Designer", "ALTX-DESIGNER", 5195, Y),
    ("Snowflake", "Support & Services", "Snowflake Priority Support", "SNOW-PRIO-SUP", ("pct", 0.10, 900), "per org / year"),
    # CRM, sales, marketing, service
    ("Salesforce", "Enterprise Software", "Sales Cloud Einstein 1 Sales", "SFDC-E1-SALES", 6000, Y),
    ("Salesforce", "Enterprise Software", "Service Cloud Unlimited", "SFDC-SVC-UNL", 3960, Y),
    ("Salesforce", "Enterprise Software", "Experience Cloud Customer Community", "SFDC-EXP-CC", 24, "per member / year"),
    ("Salesforce", "Enterprise Software", "Tableau Viewer", "SFDC-TAB-VIEW", 180, Y),
    ("Salesforce", "Enterprise Software", "Slack Enterprise Grid", "SFDC-SLACK-GRID", 360, Y),
    ("Salesforce", "Support & Services", "Salesforce Premier Success Plan", "SFDC-PREMIER", ("pct", 0.30, 1800), "per org / year"),
    ("Salesforce", "Support & Services", "Salesforce Signature Success Plan", "SFDC-SIGNATURE", ("pct", 0.40, 1800), "per org / year"),
    ("Salesforce", "Support & Services", "Salesforce Implementation (partner)", "SFDC-IMPL", ("fee", 45000), "one-off per org"),
    ("HubSpot", "Enterprise Software", "Service Hub Enterprise", "HS-SVC-ENT", 1800, Y),
    ("HubSpot", "Enterprise Software", "Content Hub Enterprise", "HS-CONTENT-ENT", 18000, "per org / year"),
    ("HubSpot", "Support & Services", "HubSpot Onboarding", "HS-ONBOARD", ("fee", 3500), "one-off per org"),
    ("Pipedrive", "Enterprise Software", "Pipedrive Enterprise", "PD-ENT", 1188, Y),
    ("Gong", "Enterprise Software", "Gong Revenue Intelligence", "GONG-RI", 1600, Y),
    ("Outreach", "Enterprise Software", "Outreach Sales Engagement", "OUTREACH-SE", 1300, Y),
    ("Salesloft", "Enterprise Software", "Salesloft Advanced", "SLOFT-ADV", 1500, Y),
    ("LinkedIn", "Enterprise Software", "Sales Navigator Advanced Plus", "LI-SN-ADVPLUS", 1600, Y),
    ("ZoomInfo", "Enterprise Software", "ZoomInfo SalesOS Advanced", "ZI-ADV", 25000, "per org / year"),
    ("Adobe", "Enterprise Software", "Adobe Experience Manager Sites", "ADBE-AEM", 250000, "per org / year"),
    ("Adobe", "Enterprise Software", "Adobe Marketo Engage Prime", "ADBE-MKTO", 60000, "per org / year"),
    ("Adobe", "Enterprise Software", "Adobe Acrobat Sign Enterprise", "ADBE-SIGN-ENT", 480, Y),
    ("Intercom", "Enterprise Software", "Intercom Expert", "ICOM-EXPERT", 1584, "per seat / year"),
    ("Freshworks", "Enterprise Software", "Freshdesk Enterprise", "FW-FD-ENT", 948, "per agent / year"),
    ("Genesys", "Enterprise Software", "Genesys Cloud CX 3", "GEN-CX3", 1860, "per agent / year"),
    ("NICE", "Enterprise Software", "CXone Mpower Complete", "NICE-CXONE", 2400, "per agent / year"),
    ("Five9", "Enterprise Software", "Five9 Optimum", "FIVE9-OPT", 2100, "per agent / year"),
    ("Twilio", "Enterprise Software", "Twilio Flex", "TWLO-FLEX", 1800, "per agent / year"),
    # ITSM, HR, finance, procurement, legal
    ("ServiceNow", "Enterprise Software", "IT Operations Management Professional", "SNOW-ITOM-PRO", 180, "per node / year"),
    ("ServiceNow", "Enterprise Software", "Customer Service Management Professional", "SNOW-CSM-PRO", 1680, "per agent / year"),
    ("ServiceNow", "Support & Services", "ServiceNow Impact", "SNOW-IMPACT", ("pct", 0.15, 1800), "per org / year"),
    ("ServiceNow", "Support & Services", "ServiceNow Implementation (partner)", "SNOW-IMPL", ("fee", 90000), "one-off per org"),
    ("Ivanti", "Enterprise Software", "Ivanti Neurons for ITSM", "IVANTI-ITSM", 1080, "per agent / year"),
    ("Freshworks", "Enterprise Software", "Freshservice Enterprise", "FW-FS-ENT", 1428, "per agent / year"),
    ("Workday", "Enterprise Software", "Workday Adaptive Planning", "WDAY-ADAPTIVE", 1800, Y),
    ("Workday", "Enterprise Software", "Workday Payroll", "WDAY-PAYROLL", 120, "per employee / year"),
    ("Workday", "Support & Services", "Workday Deployment (partner)", "WDAY-DEPLOY", ("fee", 150000), "one-off per org"),
    ("SAP", "Enterprise Software", "SAP Concur Expense Standard", "SAP-CONCUR-EXP", 108, "per active user / year"),
    ("SAP", "Enterprise Software", "SAP Fieldglass", "SAP-FIELDGLASS", 120000, "per org / year"),
    ("SAP", "Support & Services", "SAP Enterprise Support", "SAP-ENT-SUP", ("pct", 0.22, 2400), "per org / year"),
    ("Oracle", "Enterprise Software", "NetSuite ERP", "ORCL-NETSUITE", 1188, Y),
    ("Oracle", "Support & Services", "Oracle Premier Support", "ORCL-PREMIER", ("pct", 0.22, 5400), "per org / year"),
    ("Coupa", "Enterprise Software", "Coupa Procurement", "COUPA-PROC", 150000, "per org / year"),
    ("Anaplan", "Enterprise Software", "Anaplan Professional", "ANAPLAN-PRO", 3000, Y),
    ("BambooHR", "Enterprise Software", "BambooHR Pro", "BHR-PRO", 108, "per employee / year"),
    ("Personio", "Enterprise Software", "Personio Professional", "PERSONIO-PRO", 132, "per employee / year"),
    ("Deel", "Enterprise Software", "Deel HR Enterprise", "DEEL-HR", 60, "per employee / year"),
    ("Rippling", "Enterprise Software", "Rippling Unity Pro", "RIPPLING-PRO", 180, "per employee / year"),
    ("Cornerstone", "Enterprise Software", "Cornerstone Learning", "CSOD-LEARN", 96, "per employee / year"),
    ("LinkedIn", "Enterprise Software", "LinkedIn Learning Enterprise", "LI-LEARN-ENT", 360, Y),
    ("Ironclad", "Enterprise Software", "Ironclad CLM", "IRONCLAD-CLM", 60000, "per org / year"),
    ("DocuSign", "Enterprise Software", "DocuSign CLM", "DS-CLM", 90000, "per org / year"),
    ("Icertis", "Enterprise Software", "Icertis Contract Intelligence", "ICERTIS-ICI", 200000, "per org / year"),
    # Security
    ("CrowdStrike", "Security Software", "Falcon Enterprise", "CRWD-FALCON-ENT", 185, "per endpoint / year"),
    ("CrowdStrike", "Security Software", "Falcon Complete", "CRWD-FALCON-CMP", 320, "per endpoint / year"),
    ("SentinelOne", "Security Software", "Singularity Complete", "S1-COMPLETE", 180, "per endpoint / year"),
    ("Microsoft", "Security Software", "Defender for Endpoint P2", "MS-MDE-P2", 62, Y),
    ("Zscaler", "Security Software", "Zscaler Internet Access Business", "ZS-ZIA-BUS", 108, Y),
    ("Zscaler", "Security Software", "Zscaler Private Access", "ZS-ZPA", 120, Y),
    ("Palo Alto Networks", "Security Software", "Prisma Access Enterprise", "PANW-PRISMA", 180, Y),
    ("Palo Alto Networks", "Security Software", "Cortex XDR Pro", "PANW-XDR-PRO", 150, "per endpoint / year"),
    ("Netskope", "Security Software", "Netskope SSE", "NTSK-SSE", 120, Y),
    ("Cloudflare", "Security Software", "Cloudflare Zero Trust", "CF-ZT", 84, Y),
    ("Okta", "Security Software", "Okta Identity Governance", "OKTA-OIG", 108, Y),
    ("Okta", "Support & Services", "Okta Premier Success", "OKTA-PREMIER", ("pct", 0.15, 168), "per org / year"),
    ("Microsoft", "Security Software", "Entra ID P2", "MS-ENTRA-P2", 108, Y),
    ("CyberArk", "Security Software", "CyberArk Privilege Cloud", "CYBR-PC", 2400, "per privileged user / year"),
    ("1Password", "Security Software", "1Password Enterprise", "1PW-ENT", 96, Y),
    ("KnowBe4", "Security Software", "KnowBe4 Platinum", "KB4-PLAT", 30, Y),
    ("Proofpoint", "Security Software", "Proofpoint Email Protection", "PFPT-EMAIL", 48, Y),
    ("Mimecast", "Security Software", "Mimecast Email Security", "MIME-EMAIL", 42, Y),
    ("Tenable", "Security Software", "Tenable Vulnerability Management", "TENB-VM", 45, "per asset / year"),
    ("Rapid7", "Security Software", "InsightVM", "R7-IVM", 40, "per asset / year"),
    ("Wiz", "Security Software", "Wiz Cloud Security", "WIZ-CNAPP", 300, "per workload / year"),
    ("Fortinet", "Security Software", "FortiGate UTP Bundle", "FTNT-UTP", 6000, "per appliance / year"),
    # Cloud infrastructure
    ("AWS", "Cloud Infrastructure", "EC2 m7i.xlarge", "m7i.xlarge", 0.2016, "per instance-hour"),
    ("AWS", "Cloud Infrastructure", "EC2 c7g.2xlarge", "c7g.2xlarge", 0.29, "per instance-hour"),
    ("AWS", "Cloud Infrastructure", "EC2 g5.xlarge (GPU)", "g5.xlarge", 1.006, "per instance-hour"),
    ("AWS", "Cloud Infrastructure", "S3 Infrequent Access", "S3-IA", 0.0125, "per GB-month"),
    ("AWS", "Cloud Infrastructure", "Data Transfer Out", "AWS-DTO", 0.09, "per GB"),
    ("AWS", "Support & Services", "AWS Enterprise Support", "AWS-ENT-SUP", ("pct", 0.05, 3000), "per org / year"),
    ("Microsoft", "Cloud Infrastructure", "Azure D8s v5", "D8s_v5", 0.384, "per instance-hour"),
    ("Microsoft", "Cloud Infrastructure", "Azure SQL Database vCore GP", "AZSQL-GP", 0.505, "per vCore-hour"),
    ("Microsoft", "Cloud Infrastructure", "Azure OpenAI GPT-4o input", "AOAI-4O-IN", 2.5, "per 1M tokens"),
    ("Google Cloud", "Cloud Infrastructure", "Compute Engine n2-standard-8", "n2-standard-8", 0.388, "per instance-hour"),
    ("Google Cloud", "Cloud Infrastructure", "BigQuery On-demand", "BQ-ONDEMAND", 6.25, "per TiB scanned"),
    ("Oracle", "Cloud Infrastructure", "OCI Compute E5 OCPU", "OCI-E5", 0.03, "per OCPU-hour"),
    ("Equinix", "Cloud Infrastructure", "Colocation Cabinet 5kW", "EQX-CAB-5KW", 30000, "per cabinet / year"),
    # Professional services
    ("Accenture", "Professional Services", "Cloud Migration Services", "ACN-CLOUDMIG", 1400, "per consultant-day"),
    ("IBM Consulting", "Professional Services", "Application Management", "IBM-AMS", 900, "per FTE-day"),
    ("Cognizant", "Professional Services", "Application Development", "CTSH-APPDEV", 700, "per developer-day"),
    ("Wipro", "Professional Services", "Infrastructure Managed Services", "WIPRO-IMS", 560, "per FTE-day"),
    ("HCLTech", "Professional Services", "Digital Engineering", "HCL-DE", 650, "per developer-day"),
    ("Bain & Company", "Professional Services", "Strategy Consulting", "BAIN-STRAT", 5000, "per partner-day"),
    ("BCG", "Professional Services", "Strategy Consulting", "BCG-STRAT", 5000, "per partner-day"),
    ("Baker McKenzie", "Professional Services", "Legal Services Partner", "BAKER-PARTNER", 9000, "per partner-day"),
    ("Robert Half", "Professional Services", "IT Contractor Staffing", "RH-IT-CONTRACT", 720, "per contractor-day"),
    ("Randstad", "Professional Services", "Temporary Staffing", "RAND-TEMP", 38, "per worker-hour"),
    # Telco
    ("Verizon", "Telco", "Enterprise Unlimited Mobile", "VZ-MOBILE-ENT", 540, "per line / year", ["NA"]),
    ("AT&T", "Telco", "Business Unlimited Mobile", "ATT-MOBILE-BUS", 540, "per line / year", ["NA"]),
    ("Lumen", "Telco", "Dedicated Internet Access 1 Gbps", "LUMN-DIA-1G", 18000, "per circuit / year", ["NA"]),
    ("Colt", "Telco", "Ethernet Line 1 Gbps", "COLT-ETH-1G", 14400, "per circuit / year", ["UK", "EU", "CH"]),
    ("Orange Business", "Telco", "Business VPN 100 Mbps", "OBS-VPN-100", 11400, "per site / year", ["EU", "UK", "APAC"]),
    ("Telia", "Telco", "Enterprise Mobile Plan", "TELIA-MOBILE", 300, "per SIM / year", ["EU"]),
    ("TIM", "Telco", "TIM Business Mobile", "TIM-MOBILE", 240, "per SIM / year", ["EU"]),
    ("Singtel", "Telco", "Enterprise Mobile Plan", "SINGTEL-MOBILE", 420, "per SIM / year", ["APAC"]),
    ("Zscaler", "Telco", "SD-WAN Branch Connector", "ZS-SDWAN", 3600, "per site / year"),
    # Insurance
    ("Beazley", "Insurance", "Cyber Insurance", "BEAZ-CYBER", 6500, "per 1M limit / year", ["NA", "UK", "EU"]),
    ("AIG", "Insurance", "Directors and Officers", "AIG-DO", 9800, "per 1M limit / year"),
    ("AIG", "Insurance", "Commercial Property", "AIG-PROP", 4.6, "per 1000 insured value / year"),
    ("Hiscox", "Insurance", "Professional Indemnity", "HISCOX-PI", 5200, "per 1M limit / year", ["UK", "EU", "NA"]),
    ("Munich Re", "Insurance", "Reinsurance Treaty", "MR-TREATY", 0.95, "per 100 ceded premium", ["EU", "CH", "NA"]),
    ("Cigna", "Insurance", "Group Health", "CIGNA-HEALTH", 7800, "per employee / year", ["NA", "APAC", "UK"]),
    ("Aon", "Insurance", "Broking Fee", "AON-BROKING", 60000, "per org / year"),
    # Logistics
    ("DHL", "Logistics", "Express Worldwide Document", "DHL-EXP-DOC", 55, "per shipment"),
    ("UPS", "Logistics", "Ground Parcel", "UPS-GROUND", 11.5, "per parcel", ["NA"]),
    ("FedEx", "Logistics", "Ground Parcel", "FDX-GROUND", 11.8, "per parcel", ["NA"]),
    ("GLS", "Logistics", "Business Parcel", "GLS-PARCEL", 6.2, "per parcel", ["EU"]),
    ("DPD", "Logistics", "Classic Parcel", "DPD-CLASSIC", 6.5, "per parcel", ["EU", "UK"]),
    ("CMA CGM", "Logistics", "Ocean FCL 40ft Asia-Europe", "CMA-FCL40", 4000, "per container"),
    ("XPO", "Logistics", "LTL Freight", "XPO-LTL", 380, "per shipment", ["NA", "EU"]),
    ("GXO", "Logistics", "Contract Warehousing", "GXO-WAREHOUSE", 9.5, "per pallet-month", ["NA", "EU", "UK"]),
    # Facilities
    ("ISS Facility Services", "Facilities", "Integrated Facility Management", "ISS-IFM", 22, "per m2 / year"),
    ("Cushman & Wakefield", "Facilities", "Property Management", "CW-PM", 12, "per m2 / year"),
    ("Securitas", "Facilities", "Manned Guarding", "SECU-GUARD", 30, "per guard-hour"),
    ("G4S", "Facilities", "Manned Guarding", "G4S-GUARD", 29, "per guard-hour"),
    ("Sodexo", "Facilities", "Meal Vouchers Service Fee", "SDX-VOUCHER", 0.6, "per voucher"),
    ("Edenred", "Facilities", "Meal Vouchers Service Fee", "EDEN-VOUCHER", 0.55, "per voucher"),
    ("Iron Mountain", "Facilities", "Records Storage", "IRM-STORAGE", 4.2, "per box / year"),
    ("Rentokil Initial", "Facilities", "Washroom Services", "RTO-WASHROOM", 420, "per washroom / year"),
]

CLAUSES = {
    "software": [
        ("UpliftCap", "{cap}% annual cap"), ("PriceLock", "unit price locked for the term"),
        ("TrueDown", "seat reduction at each anniversary without penalty"),
        ("RenewalPriceProtection", "renewal at the same unit price"),
        ("MostFavouredCustomer", "list-price decreases passed through"),
        ("TerminationForConvenience", "exit at the midpoint with 90-day notice"),
        ("RampPricing", "seats ramp over the first year at the final unit price"),
    ],
    "Cloud Infrastructure": [
        ("CommitFlexibility", "committed spend portable across services"),
        ("EgressWaiver", "egress fees waived up to 10% of spend"), ("PriceLock", "committed rate locked for the term"),
    ],
    "Professional Services": [
        ("RateCardLock", "day rates fixed for the term"), ("NamedResources", "named resources, replacement subject to approval"),
        ("OutcomeFee", "share of the fee paid on milestone acceptance"),
    ],
    "Telco": [("ReRate", "tariffs re-rated to market at each anniversary"), ("SLACredits", "service credits below target availability")],
    "Insurance": [("PremiumCap", "renewal premium increase capped at {cap}%"), ("Remarketing", "remarketing right before renewal")],
    "Logistics": [("VolumeTiers", "retroactive volume tiers"), ("SurchargeCap", "fuel surcharge capped at {cap}%")],
    "Facilities": [("IndexationCap", "indexation capped at {cap}%"), ("KPIRebate", "rebate on missed service KPIs")],
}
NOTICE = {"Telco": [90, 180], "Facilities": [90, 180], "Insurance": [60, 90]}
PAYMENT_TERMS = ["Net 30", "Net 30", "Net 45", "Net 60"]
PERIODS = {
    "2025-Q4": "2025-12-15T00:00:00Z", "2026-Q1": "2026-03-20T00:00:00Z",
    "2026-Q2": "2026-06-18T00:00:00Z", "2026-Q3": "2026-09-10T00:00:00Z",
}


def is_software(category: str) -> bool:
    return category in ("Enterprise Software", "Security Software")


def list_price(price, size: str, rng: random.Random) -> float:
    if not isinstance(price, tuple):
        return float(price)
    if price[0] == "pct":
        _, share, per_seat = price
        seats = rng.uniform(0.3, 0.8) if per_seat < 300 else rng.uniform(0.08, 0.3) if per_seat < 1500 else rng.uniform(0.02, 0.08)
        return share * per_seat * SIZE_HEADS[size] * seats
    return price[1] * SIZE_FEE_SCALE[size] * rng.uniform(0.85, 1.2)


def value_band(annual_usd: float) -> str:
    for limit, label in ((100_000, "<100k"), (250_000, "100k-250k"), (500_000, "250k-500k"), (1_000_000, "500k-1m"), (5_000_000, "1m-5m")):
        if annual_usd < limit:
            return label
    return "5m+"


def annual_value(p50_usd: float, unit: str, size: str, rng: random.Random) -> float:
    if "org" in unit:
        return p50_usd
    if "/ year" in unit:
        # Cheap seats go to most employees; expensive ones (ERP, CRM, planning) to a few specialists.
        share = rng.uniform(0.3, 0.8) if p50_usd < 300 else rng.uniform(0.08, 0.3) if p50_usd < 1500 else rng.uniform(0.02, 0.08)
        return p50_usd * SIZE_HEADS[size] * share
    return rng.uniform(80_000, 4_000_000) * SIZE_FEE_SCALE[size] / 4


def rnd(x: float) -> float:
    if x >= 1000:
        return float(round(x, -1))
    if x >= 100:
        return float(round(x))
    return float(f"{x:.4f}") if x < 1 else float(f"{x:.2f}")


def generate() -> list[dict]:
    rng = random.Random(SEED)
    rows: list[dict] = []
    for entry in CATALOG:
        supplier, category, product, sku, price, unit = entry[:6]
        regions = entry[6] if len(entry) > 6 else ALL_REGIONS
        one_off = unit.startswith("one-off")
        terms = [12] if one_off else TERMS[category]
        for country, region, currency, book, labour in COUNTRIES:
            if region not in regions:
                continue
            for term in terms:
                for size in sorted(rng.sample(SIZE_BANDS, k=rng.choice([2, 3, 3, 4])), key=SIZE_BANDS.index):
                    kind = "software" if is_software(category) and not isinstance(price, tuple) else "other"
                    discount = CATEGORY_DISCOUNT[category] + SIZE_DISCOUNT[kind][size] + TERM_DISCOUNT[term]
                    discount = min(65.0, max(2.0, discount + rng.gauss(0, 3.0)))
                    local_list = list_price(price, size, rng) * book * (labour if category in LABOUR_CATEGORIES else 1.0)
                    p50 = local_list * (1 - discount / 100)
                    spread = rng.uniform(0.07, 0.16)
                    cap = rng.choice([3, 4, 5, 5, 6, 7])
                    pool = CLAUSES["software"] if is_software(category) or category == "Support & Services" else CLAUSES[category]
                    clauses = [{"type": t, "value": v.format(cap=cap)} for t, v in rng.sample(pool, k=min(len(pool), rng.choice([0, 1, 2, 2, 3])))]
                    capped = any(c["type"].endswith("Cap") for c in clauses)
                    sample = rng.choice([8, 12, 18, 26, 40, 60, 90, 140, 220, 340])
                    if country in ("PL", "AT", "BE", "DK", "BR", "SG") or size == "20000+":
                        sample = max(6, sample // 3)
                    period = rng.choice(list(PERIODS))
                    rows.append({
                        "provider": "Internal Dataset",
                        "recordId": "",
                        "supplier": supplier,
                        "category": category,
                        "product": product,
                        "sku": sku,
                        "geography": country,
                        "currency": currency,
                        "companySizeBand": size,
                        "termMonths": term,
                        "annualValueBand": value_band(annual_value(p50 / book, unit, size, rng)),
                        "unitPriceP25": rnd(p50 * (1 - spread)),
                        "unitPriceP50": rnd(p50),
                        "unitPriceP75": rnd(p50 * (1 + spread * rng.uniform(0.9, 1.4))),
                        "discountAchievedPct": round(discount, 1),
                        "upliftCapPct": cap if capped else None,
                        "noticeDays": rng.choice(NOTICE.get(category, [30, 60, 90])),
                        "paymentTerms": rng.choice(PAYMENT_TERMS),
                        "negotiatedClauses": clauses,
                        "closingPeriod": period,
                        "sampleSize": sample,
                        "source": "mock",
                        "representative": True,
                        "updatedAt": PERIODS[period],
                        "licenseRestrictions": None,
                        "industry": rng.choice(INDUSTRIES),
                        "unitMetric": unit,
                    })
    for i, row in enumerate(rows, start=1):
        row["recordId"] = f"{GENERATED_PREFIX}{i:05d}"
    return rows


# --------------------------------------------------------------------------------------------
# Benchmark reports
# --------------------------------------------------------------------------------------------
def to_usd(row: dict) -> float:
    book = {"USD": 1.0, "EUR": 1.08, "GBP": 1.27, "CHF": 1.13}[row["currency"]]
    return row["unitPriceP50"] * book


def discount_table(rows: list[dict], key: str, order: list[str] | None = None) -> list[str]:
    groups: dict[str, list[dict]] = defaultdict(list)
    for r in rows:
        groups[r[key]].append(r)
    keys = order or sorted(groups)
    lines = [f"| {key} | records | comparable deals | median discount | P25–P75 discount | median notice (days) | uplift cap share |", "|---|---:|---:|---:|---:|---:|---:|"]
    for k in keys:
        g = groups.get(k)
        if not g:
            continue
        d = sorted(r["discountAchievedPct"] for r in g)
        q = statistics.quantiles(d, n=4) if len(d) > 1 else [d[0]] * 3
        caps = sum(1 for r in g if r["upliftCapPct"] is not None) / len(g)
        lines.append(
            f"| {k} | {len(g)} | {sum(r['sampleSize'] for r in g):,} | {statistics.median(d):.1f}% | {q[0]:.1f}–{q[2]:.1f}% "
            f"| {statistics.median(r['noticeDays'] for r in g):.0f} | {caps:.0%} |")
    return lines


def price_table(rows: list[dict], limit: int = 60) -> list[str]:
    groups: dict[tuple, list[dict]] = defaultdict(list)
    for r in rows:
        groups[(r["supplier"], r["product"], r["currency"], r.get("unitMetric", ""))].append(r)
    lines = ["| supplier | product | unit | currency | P25 | median | P75 | deals |", "|---|---|---|---|---:|---:|---:|---:|"]
    for (sup, prod, cur, unit), g in sorted(groups.items(), key=lambda kv: -sum(r["sampleSize"] for r in kv[1]))[:limit]:
        lines.append(
            f"| {sup} | {prod} | {unit} | {cur} | {statistics.median(r['unitPriceP25'] for r in g):,.2f} "
            f"| {statistics.median(r['unitPriceP50'] for r in g):,.2f} | {statistics.median(r['unitPriceP75'] for r in g):,.2f} "
            f"| {sum(r['sampleSize'] for r in g):,} |")
    return lines


HEADER = (
    "> **Mock benchmark — test data only.** Generated by `backend/scripts/generate_market_benchmark_corpus.py` "
    f"from the synthetic corpus (feed `{FEED_VERSION}`). Figures are modelled, not real deals.\n"
)


def write_reports(rows: list[dict]) -> int:
    REPORTS.mkdir(parents=True, exist_ok=True)
    for old in REPORTS.glob("*.md"):
        old.unlink()
    written = 0

    def write(name: str, title: str, body: list[str]) -> None:
        nonlocal written
        (REPORTS / name).write_text(f"# {title}\n\n{HEADER}\n" + "\n".join(body) + "\n", encoding="utf-8")
        written += 1

    for country, region, currency, _, labour in COUNTRIES:
        g = [r for r in rows if r["geography"] == country]
        write(f"country-{country.lower()}.md", f"Market benchmark — {country}", [
            f"Region {region} · priced in {currency} · labour-cost index {labour:.2f} (US = 1.00) · {len(g):,} records.", "",
            "## Discount by category", "", *discount_table(g, "category"), "",
            "## Discount by company size", "", *discount_table(g, "companySizeBand", SIZE_BANDS), "",
            "## Unit prices, most traded products", "", *price_table(g, 80),
        ])
    for size in SIZE_BANDS:
        g = [r for r in rows if r["companySizeBand"] == size]
        write(f"size-{size.replace('+', 'plus')}.md", f"Market benchmark — companies with {size} employees", [
            f"{len(g):,} records.", "",
            "## Discount by category", "", *discount_table(g, "category"), "",
            "## Discount by country", "", *discount_table(g, "geography", [c[0] for c in COUNTRIES]), "",
            "## Discount by industry", "", *discount_table(g, "industry"),
        ])
    for category in sorted({r["category"] for r in rows}):
        g = [r for r in rows if r["category"] == category]
        slug = category.lower().replace(" & ", "-").replace(" ", "-")
        write(f"category-{slug}.md", f"Market benchmark — {category}", [
            f"{len(g):,} records · {len({r['supplier'] for r in g})} suppliers · {len({r['product'] for r in g})} products.", "",
            "## Discount by company size", "", *discount_table(g, "companySizeBand", SIZE_BANDS), "",
            "## Discount by country", "", *discount_table(g, "geography", [c[0] for c in COUNTRIES]), "",
            "## Discount by term (months)", "", *discount_table([{**r, "termMonths": str(r["termMonths"])} for r in g], "termMonths"), "",
            "## Unit prices", "", *price_table(g, 200),
        ])
    write("README.md", "Mock market benchmarks", [
        f"{len(rows):,} synthetic records, {sum(r['sampleSize'] for r in rows):,} modelled comparable deals, "
        f"{len({r['supplier'] for r in rows})} suppliers, {len({r['product'] for r in rows})} products, "
        f"{len(COUNTRIES)} countries, {len(SIZE_BANDS)} company-size bands, {len(INDUSTRIES)} industries.", "",
        "One report per country (`country-*.md`), company size (`size-*.md`) and category (`category-*.md`).", "",
        "## Discount by category (all countries)", "", *discount_table(rows, "category"),
    ])
    return written


def main() -> None:
    feed = json.loads(FEED.read_text(encoding="utf-8"))
    legacy = [d for d in feed["deals"] if not d["recordId"].startswith(GENERATED_PREFIX)]
    legacy_pairs = {(d["supplier"].lower(), d["product"].lower()) for d in legacy}
    # Hand-written (supplier, product) pairs are test oracles (some deliberately thin): never add to them.
    generated = [d for d in generate() if (d["supplier"].lower(), d["product"].lower()) not in legacy_pairs]
    reports = write_reports(generated)
    for d in generated:
        d.pop("unitMetric", None)
    feed["feedVersion"] = FEED_VERSION
    feed["deals"] = legacy + generated
    FEED.write_text(json.dumps(feed, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"hand-written={len(legacy)} generated={len(generated)} total={len(feed['deals'])} reports={reports}")


if __name__ == "__main__":
    main()
