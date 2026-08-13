# Class Diagram: Analysis

```mermaid
classDiagram
    direction LR
    classDef external fill:#f9f9f9,stroke:#333,stroke-dasharray: 5 5
    class A11Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~DrawView~ Draws
        +List~string~ Headers
        +List~string~ ChartLabels
        +List~int~ ChartValues
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A11Model
    class DrawData {
        +List~DrawView~ Draws
        +List~string~ Headers
        +List~string~ ChartLabels
        +List~int~ ChartValues
    }
    class A110Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyByMonthDayWeekResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A110Model
    class FrequencyByMonthDayWeekResult {
        +string Month
        +int DayOfMonth
        +string DayOfWeek
        +int Number
        +int Count
        +double Chance
        +string Type
    }
    class A111Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~PositionFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A111Model
    class PositionFrequencyResult {
        +string Position
        +int Number
        +int Count
        +double Chance
    }
    class A112Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~BonusBallFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A112Model
    class BonusBallFrequencyResult {
        +int Number
        +int Count
        +double Chance
    }
    class A113Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~BonusBallByWeekdayFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A113Model
    class BonusBallByWeekdayFrequencyResult {
        +string DayOfWeek
        +int Number
        +int Count
        +double Chance
    }
    class A114Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~BonusBallByMonthFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A114Model
    class BonusBallByMonthFrequencyResult {
        +string Month
        +int Number
        +int Count
        +double Chance
    }
    class A115Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~BonusBallByDayOfMonthFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A115Model
    class BonusBallByDayOfMonthFrequencyResult {
        +int DayOfMonth
        +int Number
        +int Count
        +double Chance
    }
    class A116Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~BonusBallByYearFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A116Model
    class BonusBallByYearFrequencyResult {
        +int Year
        +int Number
        +int Count
        +double Chance
    }
    class A117Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~BonusBallByDayMonthWeekFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A117Model
    class BonusBallByDayMonthWeekFrequencyResult {
        +int DayOfMonth
        +string DayOfWeek
        +int Number
        +int Count
        +double Chance
    }
    class A118Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~BonusBallByMonthDayWeekFrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A118Model
    class BonusBallByMonthDayWeekFrequencyResult {
        +string Month
        +int DayOfMonth
        +string DayOfWeek
        +int Number
        +int Count
        +double Chance
    }
    class A12Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~DrawView~ Draws
        +List~string~ Headers
        +List~string~ ChartLabels
        +List~int~ ChartValues
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A12Model
    class A13Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~DrawView~ Stats
        +List~string~ Headers
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A13Model
    class DrawStatistics {
        +int Sum
        +int SumUnpicked
        +double Average
        +double Median
        +bool HasRepeatsFromPrevious
        +double Variance
        +double StdDeviation
        +int Max
        +int Min
        +int EvenCount
        +int OddCount
        +int PrimeCount
        +int CompositeCount
        +int LowCount
        +int HighCount
        ...
    }
    class A14Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A14Model
    class FrequencyResult {
        +int Number
        +int Count
        +double Chance
        +string Type
    }
    class A15Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyByWeekdayResult~ Results
        +Dictionary~string, List~FrequencyByWeekdayResult~~ GroupedResults
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A15Model
    class FrequencyByWeekdayResult {
        +string DayOfWeek
        +int Number
        +int Count
        +double Chance
        +string Type
    }
    class A16Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyByMonthResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A16Model
    class FrequencyByMonthResult {
        +string Month
        +int Number
        +int Count
        +double Chance
        +string Type
    }
    class A17Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyByDayOfMonthResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A17Model
    class FrequencyByDayOfMonthResult {
        +int DayOfMonth
        +int Number
        +int Count
        +double Chance
        +string Type
    }
    class A18Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyByYearResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A18Model
    class FrequencyByYearResult {
        +int Year
        +int Number
        +int Count
        +double Chance
        +string Type
    }
    class A19Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyByDayMonthWeekResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A19Model
    class FrequencyByDayMonthWeekResult {
        +int DayOfMonth
        +string DayOfWeek
        +int Number
        +int Count
        +double Chance
        +string Type
    }
    class A21Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A21Model
    class FrequencyResult {
        +int Number
        +double TotalFrequency
        +double Last10Frequency
        +double Last50Frequency
        +double Last100Frequency
    }
    class A22Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~FrequencyResult~ Results
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A22Model
    class FrequencyResult {
        +int Number
        +double TotalFrequency
        +double Last10Frequency
        +double Last50Frequency
        +double Last100Frequency
    }
    class FrequencyTracker {
        +Add(double)
        +GetMostFrequent()
    }
    class A23Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~PauseMapResult~ Results
        +bool HasTime
        +bool HasBonus
        +int TotalBalls
        +Dispose()
        +OnGetAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A23Model
    class PauseMapResult {
        +int Draw
        +string Date
        +string? Time
        +Dictionary~int, int~ BallPauses
        +Dictionary~int, int~ BonusPauses
    }
    class A31Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~MapRowResult~? MapData
        +int MaxBallNumber
        +int NumberOfBalls
        +bool UseBonus
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A31Model
    class MapRowResult {
        +int Draw
        +string Date
        +string? Time
        +List~int~ BallNumbers
        +int BonusNumber
    }
    class A310Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~RowDistributionResult~ RowDistributionResults
        +int Rows
        +int Cols
        +int BallsPerDraw
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A310Model
    class RowDistributionResult {
        +int Row
        +int Even
        +int Odd
        +int Count
        +double Chance
    }
    class A311Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~ColumnDistributionResult~ ColumnDistributionResults
        +int Rows
        +int Cols
        +int BallsPerDraw
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A311Model
    class ColumnDistributionResult {
        +int ColumnNumber
        +string NumbersCombination
        +int Count
        +double Chance
    }
    class A312Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~SquareDistributionResult~ SquareDistributionResults
        +int Rows
        +int Cols
        +int BallsPerDraw
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A312Model
    class SquareDistributionResult {
        +int SquareNumber
        +int EvenCount
        +int OddCount
        +int Count
        +string SquareCells
        +double Chance
    }
    class A313Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~SumDistributionResult~ SumDistributionResults
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A313Model
    class SumDistributionResult {
        +int SumHit
        +int SumMissed
        +int Frequency
        +double Probability
    }
    class A314Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~AvgMedDistributionResult~ AvgMedDistributionResults
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A314Model
    class AvgMedDistributionResult {
        +double Average
        +double Median
        +int Frequency
        +double Probability
    }
    class A315Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~VarianceStdDevResult~ VarianceStdDevResults
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A315Model
    class VarianceStdDevResult {
        +double Variance
        +double StdDev
        +int Frequency
        +double Probability
    }
    class A316Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~MinMaxDistributionResult~ MinMaxDistributionResults
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A316Model
    class MinMaxDistributionResult {
        +int Min
        +int Max
        +int Frequency
        +double Probability
    }
    class A317Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~PrimeCompositeResult~ PrimeCompositeResults
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A317Model
    class PrimeCompositeResult {
        +int PrimeCount
        +int CompositeCount
        +int Frequency
        +double Probability
    }
    class A318Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~LowHighResult~ LowHighResults
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A318Model
    class LowHighResult {
        +int LowCount
        +int HighCount
        +int Frequency
        +double Chance
    }
    class A319Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~TriSqMirResult~ TriSqMirResults
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A319Model
    class TriSqMirResult {
        +int TriangularCount
        +int SquareCount
        +int MirroredCount
        +int Frequency
        +double Chance
    }
    class A32Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +bool EnableSorting
        +string? Title
        +Dictionary~(int Number, int Position), int~? DistributionData
        +int MaxBallNumber
        +int NumberOfBalls
        +bool IsAlwaysUnordered
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A32Model
    class A320Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~PauseDistributionResult~ PauseDistributionResults
        +int NumberOfBalls
        +int MaxBallNumber
        +bool HasBonus
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A320Model
    class PauseDistributionResult {
        +string Ball
        +int PauseCount
        +int Occurrences
        +double Chance
    }
    class A321Model {
        +bool SortBalls
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~TransitionResult~ TransitionResults
        +int NumberOfBalls
        +int MaxBallNumber
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A321Model
    class TransitionResult {
        +int DrawNumber
        +string DrawDate
        +string? DrawTime
        +int Position
        +string Transition
    }
    class A33Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +int CombinationSize
        +bool IncludeBonus
        +string? Title
        +List~GeneratedFileResult~ GeneratedFiles
        +int MaxBallNumber
        +int NumberOfBalls
        +bool CanIncludeBonus
        +int MaxCombinationSize
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
    }
    class PageModel:::external
    PageModel <|-- A33Model
    class GeneratedFileResult {
        +string FileName
        +string RelativePath
    }
    class A34Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? DifferenceType
        +string? Title
        +DataTable? ResultsTable
        +int MaxBallNumber
        +int NumberOfBalls
        +bool HasBonus
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A34Model
    class A35Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +DataTable? ResultsTable
        +int NumberOfBalls
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A35Model
    class A36Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~DistributionResult~? DistributionResults
        +int NumberOfBalls
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A36Model
    class DistributionResult {
        +int EvenCount
        +int OddCount
        +int TotalCount
        +double Chance
    }
    class A37Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +Dictionary~int, List~IntervalRowResult~~ IntervalTables
        +int NumberOfBalls
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
        +OnPostDownloadChartAsync(int)
        +GetColorClassForValue(string?, int)
    }
    class PageModel:::external
    PageModel <|-- A37Model
    class IntervalRowResult {
        +string TicketRange
        +Dictionary~int, string~ Positions
    }
    class A38Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~MatrixRowResult~ MatrixResults
        +int Rows
        +int Cols
        +int BallsPerDraw
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A38Model
    class MatrixRowResult {
        +int Draw
        +string Date
        +string? Time
        +Dictionary~int, string~ Cells
    }
    class A39Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~MatrixRowResult~ MatrixResults
        +int Rows
        +int Cols
        +int BallsPerDraw
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A39Model
    class MatrixRowResult {
        +int Draw
        +string Date
        +string? Time
        +Dictionary~int, string~ Cells
        +bool IsHeader
        +bool IsReset
    }
    class A41Model {
        +string? Game
        +int? SelectedNumber
        +int? DrawCount
        +string? Direction
        +BallStatisticsResult? Statistics
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A41Model
    class BallStatisticsResult {
        +int Number
        +int Occurrences
        +string LastOccurrenceDate
        +double ProbabilityOverall
        +List~DateEntry~ DateEntries
    }
    class DateEntry {
        +string Date
        +string Time
    }
    class A42Model {
        +string? Game
        +string? Combination
        +int? DrawCount
        +string? Direction
        +CombinationStatisticsResult? Statistics
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsv()
    }
    class PageModel:::external
    PageModel <|-- A42Model
    class CombinationStatisticsResult {
        +string CombinationString
        +List~int~ CombinationNumbers
        +int Occurrences
        +string LastOccurrenceDate
        +double ProbabilityOverall
        +List~DateEntry~ DateEntries
    }
    class DateEntry {
        +string Date
        +string Time
    }
    class A51Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +bool SortBalls
        +int[,,]? TransitionMatrixByPosition
        +Dictionary~(int, int), int~ GeneralTransitionMatrix
        +List~Dictionary~(int, int), int~~ PositionTransitionMatrices
        +int NumberOfBalls
        +int MaxBallNumber
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A51Model
    class A52Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +int SelectedInterval
        +int SelectedPosition
        +Dictionary~int, int[,,]~? TransitionMatricesByInterval
        +int NumberOfBalls
        +int MaxColumn
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A52Model
    class A53Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +Dictionary~int, Dictionary~int, int[,]~~? TransitionCountByNumber
        +int NumberOfBalls
        +int MaxColumn
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A53Model
    class A54Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +List~List~int~~? PredictedNumbersByPosition
        +List~List~double~~? PredictedChancesByPosition
        +int NumberOfBalls
        +int MaxBallNumber
        +int MaxRows
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A54Model
    class A61Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +int MinLineLength
        +string? LineType
        +string? Title
        +List~LineResult~? Lines
        +int NumberOfBalls
        +int MaxBallNumber
        +bool UseBonus
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A61Model
    class LineResult {
        +int DrawNumber
        +int StartPosition
        +int EndPosition
        +int PositionNumber
        +int StartDraw
        +int EndDraw
        +string Numbers
    }
    class A62Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +int MinLineLength
        +string? LineType
        +string? Title
        +List~DiagonalLineResult~? DiagonalLines
        +int MaxBallNumber
        +int NumberOfBalls
        +bool UseBonus
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A62Model
    class DiagonalLineResult {
        +int StartDraw
        +int EndDraw
        +int StartPosition
        +int EndPosition
        +string Numbers
    }
    class A63Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +int MinHorizontalLineLength
        +int MinVerticalLineLength
        +string? Title
        +List~LineResult~? Lines
        +int MaxBallNumber
        +int NumberOfBalls
        +bool UseBonus
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A63Model
    class LineResult {
        +int StartPosition
        +int EndPosition
        +int StartDraw
        +int EndDraw
        +string LineType
        +string Numbers
    }
    class A64Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +int MinHorizontalLineLength
        +int MinVerticalLineLength
        +int MinDiagonalLineLength
        +string? Title
        +List~LineResult~? Lines
        +int MaxBallNumber
        +int NumberOfBalls
        +bool UseBonus
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        ...
    }
    class PageModel:::external
    PageModel <|-- A64Model
    class LineResult {
        +int StartPosition
        +int EndPosition
        +int StartDraw
        +int EndDraw
        +string LineType
        +string Numbers
    }
    class A65Model {
        +string? Game
        +int? DrawCount
        +string? Direction
        +string? Title
        +string? SelectedPattern
        +List~PatternMatchResult~? PatternMatches
        +int MaxBallNumber
        +int NumberOfBalls
        +bool UseBonus
        +bool HasTime
        +Dispose()
        +OnGetAsync()
        +OnPostAsync()
        +OnPostDownloadCsvAsync()
    }
    class PageModel:::external
    PageModel <|-- A65Model
    class PatternMatchResult {
        +int StartDraw
        +int StartPosition
        +int EndDraw
        +int EndPosition
        +string Numbers
    }
    class Cell {
        +int Row
        +int Col
    }

```