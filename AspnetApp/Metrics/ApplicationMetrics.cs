using System;
using Prometheus;

namespace AspnetApp.Metrics
{
    public class ApplicationMetrics
    {
        public static readonly Histogram ArticleRetrievalTime = Prometheus.Metrics.CreateHistogram("aspnetapp_article_retrieval_time_ms", "Article fetch time", new HistogramConfiguration { Buckets = new double[] { 1, 10, 20, 30, 50, 80, 100, 200, 500, 800, 1000, 2000 } });
        public static readonly Counter ArticleRetrievalByHwid = Prometheus.Metrics.CreateCounter("aspnetapp_article_retrieval_by_type_and_id_total", "Article retrieval distribution", new CounterConfiguration { LabelNames = new []{ "hwid", "type" }});
    }
}
