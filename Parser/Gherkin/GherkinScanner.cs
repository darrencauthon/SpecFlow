﻿using System;
using System.Text.RegularExpressions;
using gherkin;

namespace TechTalk.SpecFlow.Parser.Gherkin
{
    public class GherkinScanner
    {
        private readonly I18n languageService;
        private readonly GherkinBuffer buffer;

        public GherkinScanner(I18n languageService, string gherkinText)
            : this(languageService, gherkinText, 0)
        {
        }

        public GherkinScanner(I18n languageService, string gherkinText, int lineOffset)
        {
            this.languageService = languageService;
            this.buffer = new GherkinBuffer(gherkinText, lineOffset);
        }

        public void Scan(IGherkinListener listener)
        {
            ListenerExtender listenerExtender = new ListenerExtender(languageService, listener, buffer);
            DoScan(listenerExtender, buffer.LineOffset, 0);
        }

        const int MAX_ERROR_RETRY = 5;
        const int SKIP_LINES_BEFORE_RETRY = 1;

        private void DoScan(ListenerExtender listenerExtender, int startLine, int errorRertyCount)
        {
            listenerExtender.LineOffset = startLine;
            var contentToScan = buffer.GetContentFrom(startLine);

            try
            {
                Lexer lexer = languageService.lexer(listenerExtender);
                lexer.scan(contentToScan, null, 0);
            }
            catch (ScanningCancelledException)
            {
                throw;
            }
            catch (LexingError lexingError)
            {
                HandleError(GetLexingError(lexingError, listenerExtender.LineOffset), lexingError, listenerExtender, errorRertyCount);
            }
            catch (ScanningErrorException scanningErrorException)
            {
                HandleError(scanningErrorException, scanningErrorException, listenerExtender, errorRertyCount);
            }
            catch (Exception ex)
            {
                HandleError(GetUnknownError(ex), ex, listenerExtender, errorRertyCount);
            }
        }

        static private readonly Regex lexingErrorRe = new Regex(@"^Lexing error on line (?<lineno>\d+):\s*'?(?<nearTo>[^\r\n']*)");
        private ScanningErrorException GetLexingError(LexingError lexingError, int lineOffset)
        {
            var match = lexingErrorRe.Match(lexingError.Message);
            if (!match.Success)
                return GetUnknownError(lexingError);

            int parserdLine = Int32.Parse(match.Groups["lineno"].Value);
            int errorLine = parserdLine - 1 + lineOffset;
            string nearTo = match.Groups["nearTo"].Value;

            string message = string.Format("Parsing error near '{0}'", nearTo);
            if (nearTo.Equals("%_FEATURE_END_%", StringComparison.CurrentCultureIgnoreCase))
                message = "Parsing error near the end of the file. Check whether the last statement is closed properly.";

            return new ScanningErrorException(message, buffer.GetLineStartPosition(errorLine));
        }

        private ScanningErrorException GetUnknownError(Exception exception)
        {
            string message = string.Format("Parsing error: {0}", exception.Message);
            return new ScanningErrorException(message);
        }

        private void HandleError(ScanningErrorException scanningErrorException, Exception originalException, ListenerExtender listenerExtender, int errorRertyCount)
        {
            RegisterError(listenerExtender.GherkinListener, scanningErrorException, originalException);

            var position = scanningErrorException.GetPosition(buffer);

//            if (position != null &&
//                position.Line + SKIP_LINES_BEFORE_RETRY <= buffer.LineCount - 1 &&
//                errorRertyCount < MAX_ERROR_RETRY)
            var lastProcessedEditorLine = listenerExtender.LastProcessedEditorLine;
            if (position != null)
                lastProcessedEditorLine = Math.Max(position.Line, lastProcessedEditorLine);

            if (lastProcessedEditorLine + SKIP_LINES_BEFORE_RETRY <= buffer.LineCount - 1 &&
                errorRertyCount < MAX_ERROR_RETRY)
            {
                var restartLineNumber = lastProcessedEditorLine + SKIP_LINES_BEFORE_RETRY;

                DoScan(
                    listenerExtender,
                    restartLineNumber,
                    errorRertyCount + 1);
            }
        }

        private void RegisterError(IGherkinListener gherkinListener, ScanningErrorException scanningErrorException, Exception originalException)
        {
            var position = scanningErrorException.GetPosition(buffer);

            gherkinListener.Error(
                scanningErrorException.Message, 
                position ?? buffer.EndPosition, 
                originalException);
        }
    }
}