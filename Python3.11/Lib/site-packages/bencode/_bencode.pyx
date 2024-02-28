# cython: language_level=3
from cpython.bool cimport PyBool_Check
from cpython.bytearray cimport PyByteArray_Check
from cpython.bytes cimport (PyBytes_Check, PyBytes_FromStringAndSize,
                            PyBytes_GET_SIZE)
from cpython.conversion cimport PyOS_snprintf
from cpython.dict cimport PyDict_Check
from cpython.list cimport PyList_Check
from cpython.long cimport PyLong_Check
from cpython.mem cimport PyMem_Free, PyMem_Malloc
from cpython.tuple cimport PyTuple_Check
from cpython.unicode cimport PyUnicode_Check
from libc.stdint cimport int64_t, uint8_t
from libc.string cimport memcpy, strchr


cdef extern from "util.h" nogil:
    int CM_Atoi(char* source, int size, int64_t* integer)

cdef extern from "sds.h" nogil:
    ctypedef char * sds
    sds sdsnewlen(void *init, size_t initlen)
    sds sdsnew(char *init)
    sds sdsempty()
    sds sdsdup( sds s)
    size_t sdslen( sds s)
    size_t sdsavail( sds s)
    void sdssetlen(sds s, size_t newlen)
    void sdsinclen(sds s, size_t inc)
    size_t sdsalloc( sds s)
    void sdssetalloc(sds s, size_t newlen)
    void sdsfree(sds s)
    sds sdsgrowzero(sds s, size_t len)
    sds sdscatlen(sds s,  void *t, size_t len)
    sds sdscat(sds s,  char *t)
    sds sdscatsds(sds s,  sds t)
    sds sdscpylen(sds s,  char *t, size_t len)
    sds sdscpy(sds s,  char *t)
    sds sdscatfmt(sds s, char *fmt, ...)
    sds sdstrim(sds s,  char *cset)
    void sdsrange(sds s, ssize_t start, ssize_t end);
    void sdsupdatelen(sds s)
    void sdsclear(sds s)
    int sdscmp( sds s1,  sds s2)
    sds *sdssplitlen( char *s, ssize_t len,  char *sep, int seplen, int *count)
    void sdsfreesplitres(sds *tokens, int count)
    void sdstolower(sds s)
    void sdstoupper(sds s)
    sds sdsfromlonglong(long long value)
    sds sdscatrepr(sds s,  char *p, size_t len_)
    sds *sdssplitargs( char *line, int *argc)
    sds sdsmapchars(sds s, char *from_,  char *to, size_t setlen)
    sds sdsjoin(char **argv, int argc, char *sep)
    sds sdsjoinsds(sds *argv, int argc,  char *sep, size_t seplen)


    sds sdsMakeRoomFor(sds s, size_t addlen)
    void sdsIncrLen(sds s, ssize_t incr)
    sds sdsRemoveFreeSpace(sds s)
    size_t sdsAllocSize(sds s)
    void *sdsAllocPtr(sds s)

    
class BTFailure(Exception):
    pass

ctypedef fused string:
    str
    bytes

# bytes.index
cdef Py_ssize_t bytes_index(const uint8_t[::1] data, int c, Py_ssize_t offset) nogil:
    cdef char* substring = strchr(<const char *>&data[offset], c)
    return <Py_ssize_t>(substring - <char*>&data[0])

cdef Py_ssize_t decode_int(const uint8_t[::1] x, Py_ssize_t *f) except? 0:
    """
    i开头 e结束 i123e
    :param x:
    :param f:
    :return:
    """
    # assert data[offset] == "i"
    f[0] += 1
    # end = data.index(b'e', offset)
    cdef Py_ssize_t end = bytes_index(x, 101, f[0])
    # number = int(data[offset:end])
    cdef int64_t n
    CM_Atoi(<char*>&x[f[0]], <int>(end-f[0]), &n)  # fixme use custom one
    if x[f[0]] == 45:  # '-'
        if x[f[0] + 1] == 48:  # ord('0')  # can not be negative
            raise ValueError
    elif x[f[0]] == 48 and end != f[0] + 1:  # 不能加多余的0
        raise ValueError
    f[0] = end + 1
    return <Py_ssize_t>n


cdef object decode_string(const uint8_t[::1] data , Py_ssize_t* offset):  # todo fused types
    """
    :param data: 3:abc
    :param offset: 偏移
    :return: 解析出来的字符串和下一个偏移
    """
    # colon = data.index(b':', offset)  # ：的索引
    # print(f"offset {offset[0]}") # todo del
    cdef Py_ssize_t colon = bytes_index(data, 58, offset[0])
    # print(f"colon: {colon}")
    cdef int64_t length
    # cdef Py_ssize_t length = int(data[offset:colon])  # 长度 fixme use custom one
    CM_Atoi(<char*>&data[offset[0]], <int>(colon-offset[0]), &length)
    if data[offset[0]] == 48 and colon != offset[0] + 1:
        raise ValueError
    colon += 1
    offset[0] = colon + <Py_ssize_t>length
    cdef bytes tmp = PyBytes_FromStringAndSize(<char*>&data[colon], length)
    try:
        # return (<bytes>data[colon:colon + length]).decode()
        return tmp.decode()
    except UnicodeDecodeError:
        return tmp

cdef list decode_list(const uint8_t[::1] data,  Py_ssize_t* offset):
    """
    l3:abci123ee
    :param data:
    :param offset: current offset pointer, will be updated to the next chunk 
    :return:
    """
    # assert data[offset] == "l"
    # ret, offset = [], offset + 1
    offset[0] += 1
    cdef:
        list ret = []
        uint8_t tmp
    tmp = data[offset[0]]
    while tmp != 101:
        # v, offset = decode_func[data[offset[0]]](data, offset)
        if tmp == 108:
            v = decode_list(data, offset)
        elif tmp == 100:
            v = decode_dict(data, offset)
        elif tmp ==105:
            v = decode_int(data, offset)
        elif 48 <=tmp <=57:
            v = decode_string(data ,offset)
        else:
            raise ValueError
        tmp = data[offset[0]]
        ret.append(v)
    offset[0] += 1
    return ret


cdef dict decode_dict(const uint8_t[::1] data ,  Py_ssize_t* offset):
    """

    :param data:
    :param offset: 偏移量
    :return:
    """
    cdef:
        dict ret = {}
        uint8_t tmp
    offset[0]+=1
    # print(f"in decode_dict offset: {offset[0]}")  # todo del
    tmp = data[offset[0]]
    while tmp != 101:  # dict 以e结束  ord(e)
        key =  decode_string(data, offset)
        # print(f"dict got key {key}") # todo del
        # print(f"now  offset is {offset[0]}")
        tmp = data[offset[0]]
        if tmp == 108:
            v = decode_list(data, offset)
        elif tmp == 100:
            v = decode_dict(data, offset)
        elif tmp ==105:
            v = decode_int(data, offset)
        elif 48 <=tmp <=57:
            v = decode_string(data ,offset)
        else:
            raise ValueError
        tmp = data[offset[0]]
        ret[key] = v
    offset[0] += 1
    return ret


# decode_func = {}
# decode_func[ord('l')] = decode_list
# decode_func[ord('d')] = decode_dict  # type: ignore
# decode_func[ord('i')] = decode_int  # type: ignore
# decode_func[ord('0')] = decode_string  # type: ignore
# decode_func[ord('1')] = decode_string  # type: ignore
# decode_func[ord('2')] = decode_string  # type: ignore
# decode_func[ord('3')] = decode_string  # type: ignore
# decode_func[ord('4')] = decode_string  # type: ignore
# decode_func[ord('5')] = decode_string  # type: ignore
# decode_func[ord('6')] = decode_string  # type: ignore
# decode_func[ord('7')] = decode_string  # type: ignore
# decode_func[ord('8')] = decode_string  # type: ignore
# decode_func[ord('9')] = decode_string  # type: ignore


cpdef object bdecode(const uint8_t[::1] data):
    """bdecode(data: bytes) -> Any

    """
    cdef:
        Py_ssize_t offset = 0
        uint8_t tmp
    tmp = data[0]
    try:
        if tmp == 108:
            v = decode_list(data, &offset)
        elif tmp == 100:
            v = decode_dict(data, &offset)
        elif tmp == 105:
            v = decode_int(data, &offset)
        elif 48 <= tmp <= 57:
            v = decode_string(data, &offset)
        else:
            raise ValueError
    except (IndexError, KeyError, ValueError):
        raise BTFailure("not a valid bencoded string")
    if offset != data.shape[0]:
        raise BTFailure("invalid bencoded value (data after valid prefix)")
    return v


cdef class Bencached:
    cdef public bytes bencoded

    def __cinit__(self, bytes s):
        self.bencoded = s  # type: bytes


cdef int encode_bencached(Bencached data, sds* r) except -1:
    cdef Py_ssize_t data_size = PyBytes_GET_SIZE(data.bencoded)
    cdef sds newsds = sdsMakeRoomFor(r[0], <size_t>data_size)
    if newsds == NULL:
        raise MemoryError
    r[0] = newsds
    memcpy(newsds+sdslen(newsds), <char*>data.bencoded, <size_t>data_size)
    sdsIncrLen(newsds, <ssize_t>data_size)


cdef int encode_int(Py_ssize_t data, sds* r) except -1:
    # cdef char buf[20]
    cdef sds newsds = sdsMakeRoomFor(r[0], 20)
    if newsds == NULL:
        raise MemoryError
    r[0] = newsds
    cdef int count = PyOS_snprintf(newsds+sdslen(newsds), 20,"i%lde", data)
    # r.write(<bytes>buf[:count])
    sdsIncrLen(newsds, <ssize_t> count)

cdef int encode_bool(bint data, sds* r) except -1:
    if data:
        return encode_int(1, r)
    else:
        return encode_int(0, r)


cdef int encode_string(str data, sds* r) except -1:
    return encode_bytes(data.encode(), r)

cdef int encode_bytes(const uint8_t[::1] data, sds* r) except -1:
    cdef:
        Py_ssize_t size = data.shape[0]
        int count
    cdef sds newsds = sdsMakeRoomFor(r[0], <size_t>size + 30)
    if newsds == NULL:
        raise MemoryError
    r[0] = newsds
    count = PyOS_snprintf(newsds+sdslen(newsds), <size_t>size + 30, "%ld:", size)
    sdsIncrLen(newsds, <ssize_t> count)
    # print(f"in encode_bytes, count = {count}")
    memcpy(newsds+sdslen(newsds), &data[0], <size_t>size)
    # r.write(<bytes>buf[:count+<int>size])
    sdsIncrLen(newsds, <ssize_t> size)
    # r.write(b''.join((str(len(data)).encode(), b':', data)))


cdef int encode_list(list data, sds* r) except -1:
    # r.write(b'l')
    cdef sds temp = sdscat(r[0], 'l')
    if temp == NULL:
        raise MemoryError
    r[0] = temp
    for i in data:
        # encode_func[type(i)](i, r)
        tp = type(i)
        if tp == Bencached:
            encode_bencached(i, r)
        elif PyLong_Check(i):
            encode_int(i, r)
        elif PyUnicode_Check(i):
            encode_string(i, r)
        elif PyBytes_Check(i) or PyByteArray_Check(i):
            encode_bytes(i, r)
        elif PyList_Check(i) or PyTuple_Check(i):
            encode_list(i ,r)
        elif PyDict_Check(i):
            encode_dict(i ,r)
        elif PyBool_Check(i):
            encode_bool(i, r)
    temp = sdscat(r[0], 'e')
    if temp == NULL:
        raise MemoryError
    r[0] = temp


cdef int encode_dict(dict data, sds* ret) except -1:
    cdef sds temp = sdscat(ret[0], 'd')
    if temp == NULL:
        raise MemoryError
    ret[0] = temp
    cdef list ilist = list(data.items()) # todo should we sort?
    ilist.sort()
    for key, v in ilist:
        # ret.write(b''.join((str(len(k)).encode(), b':', k.encode() if isinstance(k, str) else k)))
        if PyUnicode_Check(key):
            encode_string(key ,ret)
        else:
            encode_bytes(key, ret)
        tp = type(v)
        if tp == Bencached:
            encode_bencached(v, ret)
        elif PyLong_Check(v):
            encode_int(v, ret)
        elif PyUnicode_Check(v):
            encode_string(v, ret)
        elif PyBytes_Check(v) or PyByteArray_Check(v):
            encode_bytes(v, ret)
        elif PyList_Check(v) or PyTuple_Check(v):
            encode_list(v, ret)
        elif PyDict_Check(v):
            encode_dict(v, ret)
        elif PyBool_Check(v):
            encode_bool(v, ret)
    temp = sdscat(ret[0], 'e')
    if temp == NULL:
        raise MemoryError
    ret[0] = temp


# encode_func = {}
# encode_func[Bencached] = encode_bencached
# encode_func[int] = encode_int  # type: ignore
# encode_func[str] = encode_string  # type: ignore
# encode_func[bytes] = encode_bytes  # type: ignore
# encode_func[list] = encode_list  # type: ignore
# encode_func[tuple] = encode_list  # type: ignore
# encode_func[dict] = encode_dict  # type: ignore
# encode_func[bool] = encode_bool  # type: ignore


cpdef bytes bencode(object data):
    """
    bencode(data) -> bytes

    """
    cdef sds ret
    try:
        ret =  sdsempty()
        if ret == NULL:
            raise MemoryError
        if PyLong_Check(data):
            encode_int(data,  &ret)
        elif PyUnicode_Check(data):
            encode_string(data,  &ret)
        elif PyBytes_Check(data) or PyByteArray_Check(data):
            encode_bytes(data,  &ret)
        elif PyList_Check(data) or PyTuple_Check(data):
            encode_list(data,  &ret)
        elif PyDict_Check(data):
            encode_dict(data,  &ret)
        elif PyBool_Check(data):
            encode_bool(data,  &ret)
        elif type(data) == Bencached:
            encode_bencached(data, &ret)  # this is not likely to happen
        return PyBytes_FromStringAndSize(ret, <Py_ssize_t>sdslen(ret))
    finally:
        sdsfree(ret)
