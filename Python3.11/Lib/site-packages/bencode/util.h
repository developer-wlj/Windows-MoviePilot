#ifndef BENCODE_UTIL_H
#define BENCODE_UTIL_H
#include <stdint.h>

int CM_Atoi(char* source, int size, int64_t* integer);
int CM_Atof(char* source, int size, double* doubleing);

#endif