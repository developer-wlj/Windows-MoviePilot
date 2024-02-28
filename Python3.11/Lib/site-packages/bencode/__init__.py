# -*- coding: utf-8 -*-
from typing import IO

try:
    from bencode._bencode import BTFailure, bdecode, bencode  # type: ignore
except ImportError:
    from bencode.bencode import BTFailure, bdecode, bencode

loads = bdecode
dumps = bencode


def load(fp: IO[bytes]):
    return bdecode(fp.read())


def dump(obj, fp: IO[bytes]):
    fp.write(bencode(obj))


__all__ = ["bdecode", "bencode", "loads", "dumps", "load", "dump"]
