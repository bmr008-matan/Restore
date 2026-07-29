#nullable enable
using API.Reporting.Model;

namespace API.Reporting.Data
{
    /// <summary>
    /// The seam between the report engine and wherever data actually lives. Everything upstream of
    /// this interface — grouping, aggregation, rules, rendering — is oblivious to whether rows came
    /// from an Oracle package, from the caller's request body, or from the local sample store.
    ///
    /// It is also the licence boundary: the Oracle implementation is the only file in the project
    /// that touches a non-MIT dependency.
    /// </summary>
    public interface IReportDataProvider
    {
        /// <summary>Which data sets this provider is responsible for.</summary>
        DataSourceKind Kind { get; }

        /// <summary>
        /// Resolves one data set. <paramref name="inputs"/> are the data set's declared inputs already
        /// evaluated to concrete values, so an implementation only has to bind them — it never has to
        /// interpret parameters or expressions itself.
        /// </summary>
        Task<ReportDataSet> GetDataAsync(
            DataSetDef dataSet,
            IReadOnlyDictionary<string, object?> inputs,
            int maxRows,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Picks the provider for a data set. Registered as a singleton over all available providers, so
    /// adding the future query-builder source means registering one more provider and nothing else.
    /// </summary>
    public interface IReportDataProviderFactory
    {
        /// <summary>
        /// Providers are chosen by <see cref="DataSetDef.SourceKind"/>, except that a data set whose
        /// key appears in the request's pushed data is always served from that data instead — which is
        /// what lets one saved template run either against Oracle or against caller-supplied rows.
        /// </summary>
        IReportDataProvider Resolve(DataSetDef dataSet, bool hasPushedData);
    }
}
