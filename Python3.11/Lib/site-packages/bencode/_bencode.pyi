from typing import Any

class BTFailure(Exception):
    pass

def bencode(data: Any) -> bytes: ...
def bdecode(data: bytes) -> Any: ...
