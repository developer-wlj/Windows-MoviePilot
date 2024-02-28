"use strict";

Object.defineProperty(exports, "__esModule", {
  value: true
});
exports.ScopedRace = exports.ManualPromise = void 0;
var _stackTrace = require("./stackTrace");
let _Symbol$species, _Symbol$toStringTag;
/**
 * Copyright (c) Microsoft Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
_Symbol$species = Symbol.species;
_Symbol$toStringTag = Symbol.toStringTag;
class ManualPromise extends Promise {
  constructor() {
    let resolve;
    let reject;
    super((f, r) => {
      resolve = f;
      reject = r;
    });
    this._resolve = void 0;
    this._reject = void 0;
    this._isDone = void 0;
    this._isDone = false;
    this._resolve = resolve;
    this._reject = reject;
  }
  isDone() {
    return this._isDone;
  }
  resolve(t) {
    this._isDone = true;
    this._resolve(t);
  }
  reject(e) {
    this._isDone = true;
    this._reject(e);
  }
  static get [_Symbol$species]() {
    return Promise;
  }
  get [_Symbol$toStringTag]() {
    return 'ManualPromise';
  }
}
exports.ManualPromise = ManualPromise;
class ScopedRace {
  constructor() {
    this._terminateError = void 0;
    this._terminatePromises = new Map();
  }
  scopeClosed(error) {
    this._terminateError = error;
    for (const [p, e] of this._terminatePromises) {
      (0, _stackTrace.rewriteErrorMessage)(e, error.message);
      p.resolve(e);
    }
  }
  async race(promise) {
    return this._race([promise], false);
  }
  async safeRace(promise, defaultValue) {
    return this._race([promise], true, defaultValue);
  }
  async _race(promises, safe, defaultValue) {
    const terminatePromise = new ManualPromise();
    if (this._terminateError) terminatePromise.resolve(this._terminateError);
    const error = new Error('');
    this._terminatePromises.set(terminatePromise, error);
    try {
      return await Promise.race([terminatePromise.then(e => safe ? defaultValue : Promise.reject(e)), ...promises]);
    } finally {
      this._terminatePromises.delete(terminatePromise);
    }
  }
}
exports.ScopedRace = ScopedRace;