<?php

$wgScribuntoDefaultEngine = 'luasandbox';
$wgScribuntoEngineConf['luasandbox']['class'] = EnochianReplayEngine::class;
$wgLocaltimezone = 'UTC';
$wgLanguageCode = 'en';
$wgCapitalLinks = false;
$wgEnableUploads = false;
$wgEnableEmail = false;
$wgJobRunRate = 0;
$wgCacheDirectory = '/tmp/enochian-wiki';
$wgMainCacheType = CACHE_NONE;
$wgParserCacheType = CACHE_NONE;
$wgShowExceptionDetails = true;
$wgScribuntoEngineConf['luasandbox']['cpuLimit'] = 20;
$wgScribuntoEngineConf['luasandbox']['memoryLimit'] = 134217728;
$wgMaxTemplateDepth = 100;
$wgMaxPPNodeCount = 1000000;
$wgExpensiveParserFunctionLimit = 1000;
$wgExtensionFunctions[] = 'enochianRegisterLibraries';

function enochianReadTitle($link) {
    global $enochianRequested;
    if ($link) {
        $title = \MediaWiki\Title\Title::newFromLinkTarget($link);
        if ($title->isExternal()) {
            throw new RuntimeException('External title access is not supported offline.');
        }
        $enochianRequested[$title->getPrefixedText()] = true;
    }
}

function enochianUnsupported($name) {
    global $enochianUnsupported;
    $enochianUnsupported[$name] = true;
    throw new \MediaWiki\Extension\Scribunto\Engines\LuaCommon\LuaError(
        'Unpinned runtime input: ' . $name
    );
}

function enochianRevision($revision) {
    global $enochianSources;
    if (!$revision) {
        return $revision;
    }
    $title = \MediaWiki\Title\Title::newFromPageIdentity($revision->getPage());
    $source = $enochianSources[$title->getPrefixedText()];
    $pinned = $revision instanceof \MediaWiki\Revision\MutableRevisionRecord
        ? clone $revision
        : \MediaWiki\Revision\MutableRevisionRecord::newFromParentRevision($revision);
    $pinned->setId($source['revision']);
    $pinned->setTimestamp(wfTimestamp(TS_MW, $source['timestamp']));
    $pinned->setUser($revision->getUser());
    $pinned->setComment($revision->getComment());
    return $pinned;
}

function enochianMessageInputs($keys, $language = 'en') {
    $services = \MediaWiki\MediaWikiServices::getInstance();
    $code = is_string($language) ? $language : $language->getCode();
    $languages = array_unique(array_merge([$code], $services->getLanguageFallback()->getAll($code), ['en']));
    foreach ((array)$keys as $key) {
        foreach ($languages as $lang) {
            $name = 'MediaWiki:' . $services->getContentLanguage()->ucfirst($key);
            if ($lang !== 'en') {
                $name .= '/' . $lang;
            }
            enochianReadTitle(\MediaWiki\Title\Title::newFromText($name));
        }
    }
}

function enochianRegisterLibraries() {
class EnochianReplayEngine extends \MediaWiki\Extension\Scribunto\Engines\LuaSandbox\LuaSandboxEngine {
    protected function getLibraries($engine, array $coreLibraries = []) {
        $coreLibraries['mw.title'] = EnochianReplayTitleLibrary::class;
        $coreLibraries['mw.message'] = EnochianReplayMessageLibrary::class;
        $coreLibraries['mw.text'] = EnochianReplayTextLibrary::class;
        $coreLibraries['mw.site'] = EnochianReplaySiteLibrary::class;
        $coreLibraries['mw.language'] = EnochianReplayLanguageLibrary::class;
        return parent::getLibraries($engine, $coreLibraries);
    }

    public function loadJsonData($title) {
        enochianReadTitle(\MediaWiki\Title\Title::newFromText($title));
        return parent::loadJsonData($title);
    }
}

class EnochianReplayMessageLibrary extends \MediaWiki\Extension\Scribunto\Engines\LuaCommon\MessageLibrary {
    private function recordInputs($data) {
        if (!isset($data['rawMessage']) && $data['useDB']) {
            enochianMessageInputs($data['keys'], $data['lang']);
        }
    }

    public function messagePlain($data) {
        $this->recordInputs($data);
        return parent::messagePlain($data);
    }

    public function messageCheck($what, $data) {
        $this->recordInputs($data);
        return parent::messageCheck($what, $data);
    }
}

class EnochianReplayTextLibrary extends \MediaWiki\Extension\Scribunto\Engines\LuaCommon\TextLibrary {
    public function register() {
        enochianMessageInputs(['comma-separator', 'and', 'word-separator', 'ellipsis']);
        return parent::register();
    }
}

class EnochianReplaySiteLibrary extends \MediaWiki\Extension\Scribunto\Engines\LuaCommon\SiteLibrary {
    public function register() {
        enochianMessageInputs(['blanknamespace']);
        return parent::register();
    }

    public function pagesInCategory($category = null, $which = null) {
        enochianUnsupported('mw.site.pagesInCategory');
    }

    public function pagesInNamespace($ns = null) {
        enochianUnsupported('mw.site.pagesInNamespace');
    }

    public function usersInGroup($group = null) {
        enochianUnsupported('mw.site.usersInGroup');
    }

    public function interwikiMap($filter = null) {
        enochianUnsupported('mw.site.interwikiMap');
    }
}

class EnochianReplayLanguageLibrary extends \MediaWiki\Extension\Scribunto\Engines\LuaCommon\LanguageLibrary {
    public function formatDate($lang, $args) {
        if ($args[1] !== '' && !preg_match('/^\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}Z)?$/D', $args[1])) {
            enochianUnsupported('mw.language.formatDate(relative-date)');
        }
        return parent::formatDate($lang, $args);
    }
}

class EnochianReplayTitleLibrary extends \MediaWiki\Extension\Scribunto\Engines\LuaCommon\TitleLibrary {
    public function newTitle($text_or_id, $defaultNamespace = null) {
        if (is_numeric($text_or_id)) {
            enochianUnsupported('mw.title.new(numeric-id)');
        }
        return parent::newTitle($text_or_id, $defaultNamespace);
    }

    public function getExpensiveData($text) {
        global $enochianSources;
        $title = \MediaWiki\Title\Title::newFromText($text);
        enochianReadTitle($title);
        $data = parent::getExpensiveData($text);
        if ($title && $data[0] !== null) {
            $data[0]['id'] = $enochianSources[$title->getPrefixedText()]['page_id'] ?? 0;
        }
        return $data;
    }

    public function getContent($text) {
        enochianReadTitle(\MediaWiki\Title\Title::newFromText($text));
        return parent::getContent($text);
    }

    public function redirectTarget($text) {
        enochianReadTitle(\MediaWiki\Title\Title::newFromText($text));
        return parent::redirectTarget($text);
    }

    public function getPageLangCode($text) {
        global $enochianSources;
        $title = \MediaWiki\Title\Title::newFromText($text);
        enochianReadTitle($title);
        return [$enochianSources[$title->getPrefixedText()]['language'] ?? 'en'];
    }

    public function getCategories($text) {
        enochianUnsupported('mw.title.getCategories');
    }

    public function getFileInfo($text) {
        enochianUnsupported('mw.title.file');
    }

    public function protectionLevels($text) {
        enochianUnsupported('mw.title.protectionLevels');
    }

    public function cascadingProtection($text) {
        enochianUnsupported('mw.title.cascadingProtection');
    }
}
}
