using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PersonalAssistant.Features.KnowledgeBase.Models;
using Serilog;

namespace PersonalAssistant.Features.KnowledgeBase.Services;

/// <summary>
/// 知识库服务：索引本地文档（.md/.txt/.pdf 文本）并支持关键词搜索。
/// 使用 TF-IDF 风格的词频匹配，无需外部模型。
/// 索引持久化到 %APPDATA%\PersonalAssistant\knowledge_base\index.json。
/// 资源成本：仅索引和搜索时消耗 CPU，空闲时零开销。
/// </summary>
public class KnowledgeBaseService
{
    private static readonly string IndexDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "knowledge_base");

    private static readonly string IndexPath = Path.Combine(IndexDir, "index.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private KnowledgeBaseIndex? _index;
    private readonly Dictionary<string, double> _idfCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>索引状态</summary>
    public bool IsIndexed => _index is { Chunks.Count: > 0 };

    /// <summary>已索引的文档数</summary>
    public int IndexedFileCount => _index?.FileCount ?? 0;

    /// <summary>当前索引的目录</summary>
    public string? SourceDirectory => _index?.SourceDirectory;

    /// <summary>
    /// 索引指定目录中的所有支持文件（.md, .txt, .pdf 的文本提取）。
    /// </summary>
    /// <param name="directory">文档目录路径</param>
    /// <param name="progress">进度报告回调</param>
    public async Task IndexDirectoryAsync(string directory, Action<string>? progress = null)
    {
        if (!Directory.Exists(directory))
        {
            Log.Warning("[KB] 索引目录不存在: {Dir}", directory);
            throw new DirectoryNotFoundException($"目录不存在: {directory}");
        }

        progress?.Invoke("正在扫描文件...");

        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => IsSupportedFile(f))
            .ToList();

        if (files.Count == 0)
        {
            Log.Information("[KB] 目录中无支持文件: {Dir}", directory);
            throw new InvalidOperationException($"目录中无支持的文档文件（.md/.txt/.pdf）: {directory}");
        }

        var index = new KnowledgeBaseIndex { SourceDirectory = directory };
        var chunkId = 0;

        foreach (var file in files)
        {
            progress?.Invoke($"正在索引: {Path.GetFileName(file)}...");
            try
            {
                var content = ReadFileContent(file);
                var chunks = ChunkText(content, Path.GetFileName(file));
                foreach (var chunk in chunks)
                {
                    chunk.Id = $"chunk_{chunkId++}";
                    chunk.SourceFile = file;
                }
                index.Chunks.AddRange(chunks);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[KB] 无法读取文件: {File}", file);
            }
        }

        index.FileCount = files.Count;
        index.LastIndexed = DateTime.Now;

        // 构建 IDF 缓存
        BuildIdfCache(index);

        lock (_lock)
        {
            _index = index;
            SaveIndex(index);
        }

        Log.Information("[KB] 索引完成: {Chunks} 个块, {Files} 个文件",
            index.Chunks.Count, index.FileCount);
    }

    /// <summary>
    /// 搜索知识库，返回最相关的文档块。
    /// 使用 TF-IDF 余弦相似度评分。
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="topK">返回结果数</param>
    public List<SearchResult> Search(string query, int topK = 5)
    {
        lock (_lock)
        {
            if (_index is null || _index.Chunks.Count == 0)
                return new List<SearchResult>();
        }

        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
            return new List<SearchResult>();

        // 计算查询 TF 向量
        var queryTf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in queryTerms)
            queryTf[term] = queryTf.TryGetValue(term, out var v) ? v + 1.0 : 1.0;

        // 归一化
        var queryNorm = Math.Sqrt(queryTf.Values.Sum(v => v * v));
        if (queryNorm > 0)
        {
            foreach (var key in queryTf.Keys)
                queryTf[key] /= queryNorm;
        }

        // 对每个块评分
        var results = new List<SearchResult>();
        lock (_lock)
        {
            foreach (var chunk in _index!.Chunks)
            {
                var score = ComputeCosineSimilarity(chunk.Content, queryTf);
                if (score > 0)
                {
                    results.Add(new SearchResult
                    {
                        Chunk = chunk,
                        Score = score
                    });
                }
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>加载已保存的索引</summary>
    public void LoadIndex()
    {
        if (!File.Exists(IndexPath))
            return;

        try
        {
            var json = File.ReadAllText(IndexPath);
            var index = JsonSerializer.Deserialize<KnowledgeBaseIndex>(json, JsonOptions);
            if (index?.Chunks.Count > 0)
            {
                lock (_lock)
                {
                    _index = index;
                    BuildIdfCache(index);
                }
                Log.Information("[KB] 加载索引: {Chunks} 个块", index.Chunks.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[KB] 加载索引失败");
        }
    }

    private static void SaveIndex(KnowledgeBaseIndex index)
    {
        if (!Directory.Exists(IndexDir))
            Directory.CreateDirectory(IndexDir);
        var json = JsonSerializer.Serialize(index, JsonOptions);
        File.WriteAllText(IndexPath, json);
    }

    private void BuildIdfCache(KnowledgeBaseIndex index)
    {
        _idfCache.Clear();
        var N = index.Chunks.Count;
        if (N == 0) return;

        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in index.Chunks)
        {
            var terms = new HashSet<string>(Tokenize(chunk.Content), StringComparer.OrdinalIgnoreCase);
            foreach (var term in terms)
            {
                docFreq[term] = docFreq.TryGetValue(term, out var v) ? v + 1 : 1;
            }
        }

        foreach (var (term, df) in docFreq)
            _idfCache[term] = Math.Log((N + 1.0) / (df + 1.0)) + 1.0;
    }

    private double ComputeCosineSimilarity(string text, Dictionary<string, double> queryTf)
    {
        var terms = Tokenize(text);
        var docTf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
            docTf[term] = docTf.TryGetValue(term, out var v) ? v + 1.0 : 1.0;

        double dotProduct = 0, docNorm2 = 0;
        foreach (var (term, tf) in docTf)
        {
            var idf = _idfCache.TryGetValue(term, out var v) ? v : 1.0;
            var tfidf = tf * idf;
            docNorm2 += tfidf * tfidf;

            if (queryTf.TryGetValue(term, out var qtf))
                dotProduct += qtf * tfidf;
        }

        var docNorm = Math.Sqrt(docNorm2);
        return docNorm > 0 ? dotProduct / docNorm : 0;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();

        // 中文：按字符分割
        var chineseChars = Regex.Matches(text, @"[\u4e00-\u9fff]+");
        foreach (Match match in chineseChars)
        {
            foreach (char c in match.Value)
                tokens.Add(c.ToString());
        }

        // 英文/数字：按单词分割
        var englishWords = Regex.Matches(text, @"[a-zA-Z0-9]+");
        foreach (Match match in englishWords)
            tokens.Add(match.Value.ToLowerInvariant());

        return tokens;
    }

    private static List<DocumentChunk> ChunkText(string content, string fileName, int chunkSize = 512, int overlap = 128)
    {
        var chunks = new List<DocumentChunk>();
        var offset = 0;

        while (offset < content.Length)
        {
            var end = Math.Min(offset + chunkSize, content.Length);

            // 尽量在句子或段落边界断开
            if (end < content.Length)
            {
                var breakPoint = FindBreakPoint(content, end - 50, end);
                if (breakPoint > offset)
                    end = breakPoint;
            }

            var chunkContent = content[offset..end];
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                chunks.Add(new DocumentChunk
                {
                    Content = chunkContent.Trim(),
                    CharOffset = offset,
                    SourceFile = fileName,
                    IndexedAt = DateTime.Now
                });
            }

            offset = end - overlap;
            if (offset >= content.Length) break;
        }

        return chunks;
    }

    private static int FindBreakPoint(string text, int start, int end)
    {
        for (var i = end; i >= start; i--)
        {
            if (text[i] is '\n' or '。' or '.' or '！' or '!' or '？' or '?')
                return i + 1;
        }
        return start;
    }

    private static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".md" or ".txt" or ".pdf";
    }

    private static string ReadFileContent(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".pdf")
        {
            // 简单 PDF 文本提取（仅提取文本流，不支持复杂 PDF）
            return ExtractPdfText(path);
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    /// <summary>
    /// 简易 PDF 文本提取：提取流中的可读文本。
    /// 完整 PDF 解析需要第三方库（如 iTextSharp），此处提供基础提取。
    /// </summary>
    private static string ExtractPdfText(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var raw = Encoding.UTF8.GetString(bytes);

            // 提取 BT...ET 块之间的文本
            var result = new StringBuilder();
            var matches = Regex.Matches(raw, @"BT\s*(.*?)\s*ET", RegexOptions.Singleline);
            foreach (Match match in matches)
            {
                var textOps = Regex.Matches(match.Groups[1].Value, @"\((.*?)\)\s*Tj");
                foreach (Match op in textOps)
                    result.AppendLine(op.Groups[1].Value);
            }

            var text = result.ToString().Trim();
            if (text.Length > 0)
                return text;

            // 回退：尝试提取所有可读文本
            var cleaned = Regex.Replace(raw, @"[^\x20-\x7E\u4e00-\u9fff\u3000-\u303f\uff00-\uffef\n\r]", " ");
            return string.Join("\n", cleaned.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 20));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[KB] PDF 文本提取失败: {Path}", path);
            return string.Empty;
        }
    }
}

/// <summary>
/// 搜索结果项
/// </summary>
public class SearchResult
{
    public DocumentChunk Chunk { get; init; } = null!;
    public double Score { get; init; }

    public string Preview => Chunk.Content.Length > 200
        ? Chunk.Content[..200] + "..."
        : Chunk.Content;
}
