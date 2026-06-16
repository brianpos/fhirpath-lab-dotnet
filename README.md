# FhirPathLab-DotNetEngine

## Overview
The fhirpath-lab is a dedicated tool for testing out fhirpath expressions against the various
fhirpath execution engines available - dotnet (Firely), java (HAPI) and javascript (nlm)
This project implements the dotnet engine in the Firely SDK.

Issues will be managed through the fhirpath-lab project, and not this specific repo's issue list.

## New Features Added:

### 16 June 2026
* Update to the Firely SDK 6.2.0 (R6 release line)
* Removed the FhirPath expression validator (expectedReturnType/parseDebug/parseDebugTree) as the
  `brianpos.Fhir.Base.FhirPath.Validator` package does not yet have a 6.x compatible release
* Migrated the evaluation engine to the new `PocoNode` FhirPath API

### 3 June 2026
* Update to the Firely SDK 5.13.4

### 28 April 2026
* Update to the Firely SDK 5.13.2
* Use the allow sites list from my diff project https://raw.githubusercontent.com/brianpos/hl7-diff/main/public/allowed-sites.json

### 29 August 2025
* Handle XML or JSON embedded content (as alternative to parameters.part.resource - supports wrong model and also cross typing xml wrapped in json)

### 28 August 2025
* Update to the Firely SDK 5.12.2
* Update FhirPath Validator
* Add support for debug tracer introduced in Firely SDK 5.12.2

### 25 July 2025
* Update to the Firely SDK 5.12.1

### 11 February 2025
* Update to the Firely SDK 5.11.3

### 23 January 2025
* Update to the Firely SDK 5.11.1

### 5 July 2023
* Variable Support added to the expression validator
