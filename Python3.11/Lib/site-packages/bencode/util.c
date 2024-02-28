#include "stdio.h"
#include "util.h"


long long pow1(int x,int y)
{
	long long num = 1;
	int i;

	for(i = 0; i < y; i++)
	{
		num = num*x;
	}

	return num;
}



int CM_Atoi(char* source, int size, int64_t* integer)
{
	int offset1,offset2;
    int64_t num;
	int signedflag;//+为1 -为0

	if(source == NULL || *source == 0 ||integer == NULL)
	{
		return 0;
	}

	offset1 = 0;
	offset2 = 0;
	num = 0;

	while(*source > 0 && *source <= 32)//去除首部空格 等异常字符
	{
		source++;
		offset1++;
	}

	signedflag = 1;//默认为+
	if(*source == '+')
	{
		signedflag = 1;
		source++;
		offset1++;
	}
	else if(*source == '-')
	{
		signedflag = 0;
		source++;
		offset1++;
	}

	while(*source != '\0' && *source >= '0' && *source <= '9' && ((offset1 + offset2) < size))
	{
		num = *source- '0' + num*10;
		source++;
		offset2++;
	}

	if(signedflag == 0)
	{
		num = -num;
	}

	if(offset2)
	{
		*integer = num;
		return offset1+offset2;
	}
	else
	{
		return 0;
	}
}

int CM_Atof(char* source, int size, double* doubleing)
{
	int offset1,offset2,n;
	double num;
	int signedflag;//+为1 -为0

	if(source == NULL || *source == 0 || doubleing == NULL)
	{
		return 0;
	}

	offset1 = 0;
	offset2 = 0;
	num = 0.0;

	while(*source > 0 && *source <= 32)//去除首部空格 \r \n \t \r 等异常字符
	{
		source++;
		offset1++;
	}

	signedflag = 1;//默认为+
	if(*source == '+')
	{
		signedflag = 1;
		source++;
		offset1++;
	}
	else if(*source == '-')
	{
		signedflag = 0;
		source++;
		offset1++;
	}


	//整数部分
	while(*source != '\0' && *source >= '0' && *source <= '9' && ((offset1 + offset2) < size))
	{
		num = *source- '0' + num*10.0;
		source++;
		offset2++;
	}

	if(offset2 != 0 && *source == '.')
	{
		source++;
		offset2++;

		//小数部分
		n = 0;
		while(*source != '\0' && *source >= '0' && *source <= '9' && ((offset1 + offset2) < size))
		{
			num = (*source- '0')*(1.0/pow1(10,++n)) + num;
			source++;
			offset2++;
		}
	}

	if(signedflag == 0)
	{
		num = -num;
	}

	if(offset2)
	{
		*doubleing = num;
		return offset1+offset2;
	}
	else
	{
		return 0;
	}
}