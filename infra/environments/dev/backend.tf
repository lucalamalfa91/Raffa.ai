# Task E01/F02/US01/T01 (ADR-007: remote state per environment). Remote
# state only -- HCP Terraform workspace "raffa-dev" in organization
# "raffa-platform" (bootstrapped in E01/F01/US02/T01, VCS-wired in
# E01/F01/US02/T02). No local state; state is never in git.
terraform {
  cloud {
    organization = "raffa-platform"

    workspaces {
      name = "raffa-dev"
    }
  }
}
