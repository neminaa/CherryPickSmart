# CherryPickSmart Code Organization Plan

## Current Structure Analysis

The project currently has a reasonable structure but several areas need improvement:

### Current Issues
1. **Inconsistent Naming**: File names don't always match class names (e.g., `ReportGenerator.cs` contains `HtmlReportGenerator`)
2. **Missing Interfaces**: Not all services have corresponding interfaces
3. **Mixed Concerns**: Some classes have multiple responsibilities
4. **Dependency Registration**: All DI registration is inline in Program.cs
5. **No Configuration Structure**: Configuration is handled ad-hoc
6. **Missing Cross-Cutting Concerns**: No structured approach to logging, validation, etc.

## Proposed Reorganization

### 1. Folder Structure Enhancement

```
CherryPickSmart/
├── Commands/                      # CLI Commands
│   ├── Interfaces/
│   │   └── ICommand.cs
│   ├── AnalyzeCommand.cs
│   ├── PlanCommand.cs
│   └── ExecuteCommand.cs
│
├── Core/                         # Domain Logic
│   ├── ConflictAnalysis/
│   │   ├── Interfaces/
│   │   │   └── IOrderOptimizer.cs
│   │   ├── ConflictPredictor.cs
│   │   └── OrderOptimizer.cs
│   │
│   ├── GitAnalysis/
│   │   ├── Interfaces/
│   │   │   ├── IGitHistoryParser.cs
│   │   │   └── IMergeCommitAnalyzer.cs
│   │   ├── GitHistoryParser.cs
│   │   └── MergeCommitAnalyzer.cs
│   │
│   ├── Integration/
│   │   ├── Interfaces/
│   │   │   ├── IGitCommandExecutor.cs
│   │   │   └── IJiraClient.cs
│   │   ├── GitCommandExecutor.cs
│   │   └── JiraClient.cs
│   │
│   └── TicketAnalysis/
│       ├── Interfaces/
│       │   ├── IOrphanCommitDetector.cs
│       │   ├── ITicketExtractor.cs
│       │   └── ITicketInferenceEngine.cs
│       ├── OrphanCommitDetector.cs
│       ├── TicketExtractor.cs
│       └── TicketInferenceEngine.cs
│
├── Models/                       # Data Models
│   ├── Commit.cs
│   ├── AnalysisResult.cs
│   └── Configuration/
│       ├── AppSettings.cs
│       └── JiraSettings.cs
│
├── Services/                     # Application Services
│   ├── Interfaces/
│   │   ├── IConfigurationService.cs
│   │   ├── IInteractivePromptService.cs
│   │   └── IReportGenerator.cs
│   ├── ConfigurationService.cs
│   ├── InteractivePromptService.cs
│   └── ReportGenerator.cs
│
├── Infrastructure/              # Cross-cutting concerns
│   ├── DependencyInjection/
│   │   └── ServiceCollectionExtensions.cs
│   ├── Logging/
│   │   └── LoggingExtensions.cs
│   └── Validation/
│       └── ValidationExtensions.cs
│
├── Templates/                   # Template files
│   └── Report.html.scriban
│
├── Output/                      # Generated output
├── bin/
├── obj/
├── Program.cs
└── CherryPickSmart.csproj
```

### 2. Key Improvements

#### A. Extract Interfaces
- Create interfaces for all services and core components
- Move interfaces to dedicated `Interfaces` folders within each domain

#### B. Dependency Injection Organization
- Create `ServiceCollectionExtensions.cs` to organize DI registration
- Group registrations by domain/feature

#### C. Configuration Management
- Create proper configuration models
- Implement structured configuration loading

#### D. Naming Consistency
- Rename files to match class names
- Follow consistent naming conventions

#### E. Separation of Concerns
- Ensure each class has a single responsibility
- Extract complex logic into dedicated services

### 3. Implementation Steps

1. **Phase 1: Create Infrastructure**
   - Create Infrastructure folder structure
   - Move DI registration to ServiceCollectionExtensions

2. **Phase 2: Extract Interfaces**
   - Create interface for each service
   - Update implementations to use interfaces

3. **Phase 3: Reorganize Core**
   - Move interfaces to dedicated folders
   - Ensure consistent naming

4. **Phase 4: Configuration**
   - Create configuration models
   - Update ConfigurationService

5. **Phase 5: Clean Up**
   - Fix naming inconsistencies
   - Remove unused code
   - Update documentation

### 4. Benefits

- **Better Testability**: Interfaces enable easier mocking
- **Clearer Architecture**: Organized folder structure shows intent
- **Easier Maintenance**: Single responsibility principle
- **Better Discoverability**: Consistent naming and organization
- **Scalability**: Clear patterns for adding new features

### 5. Next Steps

Would you like me to:
1. Start implementing this reorganization?
2. Focus on a specific area first?
3. Create additional documentation?
4. Set up automated code quality checks?
