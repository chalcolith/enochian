<?php

// This external MediaWiki adapter is not part of the Enochian core library.
$input = json_decode(file_get_contents('/inputs/input.json'), true, 512, JSON_THROW_ON_ERROR);
mkdir('/tmp/enochian-wiki');
$install = [
    PHP_BINARY, 'maintenance/run.php', 'install',
    '--dbtype', 'sqlite', '--dbpath', '/tmp/enochian-wiki', '--dbname', 'enochian',
    '--server', 'https://en.wiktionary.org', '--scriptpath', '/w',
    '--lang', 'en', '--extensions', 'Scribunto,ParserFunctions',
    '--pass', bin2hex(random_bytes(24)), 'English Wiktionary replay', 'ReplayAdmin'
];
$command = implode(' ', array_map('escapeshellarg', $install));
ob_start();
passthru($command, $exit);
$installationLog = ob_get_clean();
if ($exit !== 0) {
    fwrite(STDERR, $installationLog);
    exit($exit);
}
file_put_contents('LocalSettings.php', "\nrequire '/adapter/settings.php';\n", FILE_APPEND);

require 'maintenance/Maintenance.php';

class EnochianReplayMaintenance extends Maintenance {
    public function execute() {
        global $input, $enochianSources, $enochianRequested, $enochianUnsupported;
        $enochianSources = [];
        foreach ($input['sources'] as $source) {
            $enochianSources[$source['title']] = $source;
        }
        $services = \MediaWiki\MediaWikiServices::getInstance();
        $user = \MediaWiki\User\User::newSystemUser('Enochian replay', ['steal' => true]);
        if (!$user) {
            throw new RuntimeException('Unable to create the isolated replay user.');
        }
        foreach ($enochianSources as $source) {
            if ($source['revision'] === 0) {
                continue;
            }
            $title = \MediaWiki\Title\Title::newFromText($source['title']);
            if (!$title || $title->getPrefixedText() !== $source['title']) {
                throw new RuntimeException('Unsupported title normalization: ' . $source['title']);
            }
            $page = $services->getWikiPageFactory()->newFromTitle($title);
            $content = \MediaWiki\Content\ContentHandler::makeContent(
                $source['content'], $title, $source['model']
            );
            $updater = $page->newPageUpdater($user);
            $updater->setContent(\MediaWiki\Revision\SlotRecord::MAIN, $content);
            $updater->saveRevision(
                \MediaWiki\CommentStore\CommentStoreComment::newUnsavedComment('Pinned offline source')
            );
            if (!$updater->getStatus()->isOK()) {
                throw new RuntimeException('Unable to import pinned source: ' . $source['title']);
            }
            \MediaWiki\Deferred\DeferredUpdates::clearPendingUpdates();
            unset($updater, $page, $content);
            gc_collect_cycles();
        }
        \MediaWiki\Deferred\DeferredUpdates::clearPendingUpdates();
        fwrite(STDERR, 'Imported ' . count($enochianSources) . " pinned sources.\n");
        $results = [];
        foreach ($input['requests'] as $request) {
            $enochianRequested = [];
            $enochianUnsupported = [];
            $title = \MediaWiki\Title\Title::newFromText($request['title']);
            enochianReadTitle($title);
            $options = \MediaWiki\Parser\ParserOptions::newFromAnon();
            $options->setTimestamp('20261001000000');
            $options->setTemplateCallback(function ($link, $parser) {
                enochianReadTitle($link);
                $result = \MediaWiki\Parser\Parser::statelessFetchTemplate($link, $parser);
                if (isset($result['revision-record'])) {
                    $result['revision-record'] = enochianRevision($result['revision-record']);
                }
                return $result;
            });
            $options->setCurrentRevisionRecordCallback(function ($link, $parser) {
                enochianReadTitle($link);
                return enochianRevision(\MediaWiki\Parser\Parser::statelessFetchRevisionRecord($link, $parser));
            });
            $parser = $services->getParserFactory()->create();
            $output = $parser->parse($request['invocation'], $title, $options);
            $document = new DOMDocument();
            $previous = libxml_use_internal_errors(true);
            $loaded = $document->loadHTML(
                '<?xml encoding="UTF-8">' . $output->getRawText(),
                LIBXML_NONET | LIBXML_NOERROR | LIBXML_NOWARNING
            );
            libxml_clear_errors();
            libxml_use_internal_errors($previous);
            if (!$loaded) {
                throw new RuntimeException('Unable to inspect replay output.');
            }
            $xpath = new DOMXPath($document);
            $readings = [];
            foreach ($xpath->query('//*[contains(concat(" ", normalize-space(@class), " "), " IPA ")]') as $node) {
                $readings[] = trim($node->textContent);
            }
            $errors = [];
            foreach ($xpath->query('//*[contains(concat(" ", normalize-space(@class), " "), " error ") or contains(@class, "scribunto-error")]') as $node) {
                $errors[] = trim($node->textContent);
            }
            $requested = array_keys($enochianRequested);
            sort($requested, SORT_STRING);
            $unsupported = array_keys($enochianUnsupported);
            sort($unsupported, SORT_STRING);
            sort($errors, SORT_STRING);
            $results[] = [
                'id' => $request['id'], 'readings' => $readings,
                'requested' => $requested, 'errors' => array_values(array_unique($errors)),
                'unsupported' => $unsupported
            ];
            \MediaWiki\Extension\Scribunto\Scribunto::resetParserEngine($parser);
            unset($parser, $options, $output, $document, $xpath);
            gc_collect_cycles();
        }
        $this->output(json_encode([
            'mediawiki' => MW_VERSION, 'php' => PHP_VERSION,
            'lua' => phpversion('luasandbox'), 'results' => $results
        ], JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) . "\n");
        \MediaWiki\Deferred\DeferredUpdates::clearPendingUpdates();
    }
}

$maintClass = EnochianReplayMaintenance::class;
require RUN_MAINTENANCE_IF_MAIN;
