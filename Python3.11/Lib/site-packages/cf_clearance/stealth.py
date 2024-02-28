# -*- coding: utf-8 -*-
import re
from dataclasses import dataclass
from typing import Dict, Union

import pkg_resources
from playwright.async_api import BrowserContext as AsyncContext
from playwright.async_api import Page as AsyncPage
from playwright.sync_api import BrowserContext as SyncContext
from playwright.sync_api import Page as SyncPage


def from_file(name):
    """Read script from ./js directory"""
    return pkg_resources.resource_string("cf_clearance", f"js/{name}").decode()


SCRIPTS: Dict[str, str] = {
    "chrome_fp": from_file("canvas.fingerprinting.js"),
    "chrome_global": from_file("chrome.global.js"),
    "chrome_touch": from_file("emulate.touch.js"),
    "navigator_permissions": from_file("navigator.permissions.js"),
    "navigator_webdriver": from_file("navigator.webdriver.js"),
    "chrome_runtime": from_file("chrome.runtime.js"),
    "chrome_plugin": from_file("chrome.plugin.js"),
}


@dataclass
class StealthConfig:
    """
    Playwright stealth configuration that applies stealth strategies to playwright page objects.
    The stealth strategies are contained in ./js package and are basic javascript scripts that are executed
    on every page.goto() called.
    Note:
        All init scripts are combined by playwright into one script and then executed this means
        the scripts should not have conflicting constants/variables etc. !
        This also means scripts can be extended by overriding enabled_scripts generator:
        ```
        @property
        def enabled_scripts():
            yield 'console.log("first script")'
            yield from super().enabled_scripts()
            yield 'console.log("last script")'
        ```
    """

    # load script options
    chrome_fp: bool = True
    chrome_global: bool = True
    chrome_touch: bool = True
    navigator_permissions: bool = True
    navigator_webdriver: bool = True
    chrome_runtime: bool = True
    chrome_plugin: bool = True

    @property
    def enabled_scripts(self):

        if self.chrome_fp:
            yield SCRIPTS["chrome_fp"]
        if self.chrome_global:
            yield SCRIPTS["chrome_global"]
        if self.chrome_touch:
            yield SCRIPTS["chrome_touch"]
        if self.navigator_permissions:
            yield SCRIPTS["navigator_permissions"]
        if self.navigator_webdriver:
            yield SCRIPTS["navigator_webdriver"]
        if self.chrome_plugin:
            yield SCRIPTS["chrome_plugin"]
        if self.chrome_runtime:
            yield SCRIPTS["chrome_runtime"]


def sync_stealth(
    page_or_context: Union[SyncContext, SyncPage],
    config: StealthConfig = None,
    pure: bool = True,
):
    """teaches synchronous playwright Page to be stealthy like a ninja!"""
    for script in (config or StealthConfig()).enabled_scripts:
        page_or_context.add_init_script(script)
    if pure:
        page_or_context.route(
            re.compile(
                r"(.*\.png(\?.*|$))|(.*\.jpg(\?.*|$))|(.*\.jpeg(\?.*|$))|(.*\.css(\?.*|$))"
            ),
            lambda route: route.abort("blockedbyclient"),
        )


async def async_stealth(
    page_or_context: Union[AsyncContext, AsyncPage],
    config: StealthConfig = None,
    pure: bool = True,
):
    """teaches asynchronous playwright Page to be stealthy like a ninja!"""
    for script in (config or StealthConfig()).enabled_scripts:
        await page_or_context.add_init_script(script)
    if pure:
        await page_or_context.route(
            re.compile(
                r"(.*\.png(\?.*|$))|(.*\.jpg(\?.*|$))|(.*\.jpeg(\?.*|$))|(.*\.css(\?.*|$))"
            ),
            lambda route: route.abort("blockedbyclient"),
        )
