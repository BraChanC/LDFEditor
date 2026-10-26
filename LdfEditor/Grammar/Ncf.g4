/*
 * LIN Node Capability File (NCF) grammar for ANTLR 4.
 * Based on LIN Node Capability Language / ISO 17987-2 / SAE J2602-3
 * and Vector LDF Explorer ModelWriter.WriteNCF output shape.
 */
grammar Ncf;

ncfFile
    : 'node_capability_file' ';' header* nodeDef* EOF
    ;

header
    : languageVersion
    | fileRevision
    | sigByteOrder
    ;

languageVersion
    : 'LIN_language_version' '=' STRING ';'
    ;

fileRevision
    : 'NCF_file_revision' '=' STRING ';'
    ;

sigByteOrder
    : 'LIN_sig_byte_order_big_endian' ';'
    | 'LIN_sig_byte_order_little_endian' ';'
    ;

nodeDef
    : 'node' identifier '{' nodeSection* '}'
    ;

nodeSection
    : generalDef
    | diagnosticDef
    | framesDef
    | encodingDef
    | statusManagement
    | freeText
    ;

/* ---- general ---- */

generalDef
    : 'general' '{' generalItem* '}'
    ;

generalItem
    : 'LIN_protocol_version' '=' STRING ';'
    | 'supplier' '=' number ';'
    | 'function' '=' number ';'
    | 'variant' '=' number ';'
    | 'bitrate' '=' bitrateDef ';'
    | 'sends_wake_up_signal' '=' STRING ';'
    | 'response_tolerance' '=' number '%' ';'
    | 'wakeup_time' '=' number 'ms' ';'
    | 'poweron_time' '=' number 'ms' ';'
    ;

bitrateDef
    : number 'kbps'
    | 'automatic' ('min' number 'kbps')? ('max' number 'kbps')?
    | 'select' '{' number 'kbps' (',' number 'kbps')* '}'
    ;

/* ---- diagnostic ---- */

diagnosticDef
    : 'diagnostic' '{' diagnosticItem* '}'
    ;

diagnosticItem
    : 'NAD' '=' nadValue ';'
    | 'diagnostic_class' '=' INT ';'
    | 'P2_min' '=' number 'ms' ';'
    | 'ST_min' '=' number 'ms' ';'
    | 'N_As_timeout' '=' number 'ms' ';'
    | 'N_Cr_timeout' '=' number 'ms' ';'
    | 'support_sid' '{' number (',' number)* '}' ';'
    | 'max_message_length' '=' INT ';'
    ;

nadValue
    : number 'to' number
    | number (',' number)+
    | number
    ;

/* ---- frames ---- */

framesDef
    : 'frames' '{' frameRole* '}'
    ;

frameRole
    : 'publish' identifier '{' frameProp* '}'
    | 'subscribe' identifier '{' frameProp* '}'
    ;

frameProp
    : 'message_ID' '=' number ';'
    | 'frame_id' '=' number ';'
    | 'length' '=' INT ';'
    | 'frame_length' '=' INT ';'
    | 'min_period' '=' number 'ms' ';'
    | 'max_period' '=' number 'ms' ';'
    | 'event_triggered_frame' '=' identifier ';'
    | 'signals' '{' signalDef* '}'
    ;

signalDef
    : identifier '{' signalProp* '}'
    ;

signalProp
    : 'init_value' '=' initValue ';'
    | 'size' '=' INT ';'
    | 'offset' '=' INT ';'
    | 'encoding' identifier '{' encodingValue* '}' ';'
    | identifier ';'
    ;

initValue
    : INT
    | '{' INT (',' INT)* '}'
    ;

/* ---- encoding (LIN 2.1+) ---- */

encodingDef
    : 'encoding' '{' encodingType* '}'
    ;

encodingType
    : identifier '{' encodingValue* '}'
    ;

encodingValue
    : 'logical_value' ',' INT (',' STRING)? ';'
    | 'physical_value' ',' INT ',' INT ',' number ',' number (',' STRING)? ';'
    | 'bcd_value' ';'
    | 'ascii_value' ';'
    | 'float_value' ';'
    ;

/* ---- status_management ---- */

statusManagement
    : 'status_management' '{' statusItem* '}'
    ;

statusItem
    : 'response_error' '=' identifier ';'
    | 'reset_indication' '=' identifier ';'
    | 'fault_state_signals' '=' identifier (',' identifier)* ';'
    ;

freeText
    : 'free_text' '{' STRING? '}'
    ;

/* ---- shared ---- */

number
    : '-'? INT
    | '-'? FLOAT
    ;

identifier
    : IDENTIFIER
    ;

IDENTIFIER
    : [a-zA-Z_] [a-zA-Z0-9_]*
    ;

FLOAT
    : [0-9]+ '.' [0-9]* ([eE] [+-]? [0-9]+)?
    | '.' [0-9]+ ([eE] [+-]? [0-9]+)?
    | [0-9]+ [eE] [+-]? [0-9]+
    ;

INT
    : '0' [xX] [0-9a-fA-F]+
    | [0-9]+
    ;

STRING
    : '"' ( ~["\\] | '\\' . )* '"'
    ;

LINE_COMMENT
    : '//' ~[\r\n]* -> channel(HIDDEN)
    ;

BLOCK_COMMENT
    : '/*' .*? '*/' -> channel(HIDDEN)
    ;

WS
    : [ \t\r\n]+ -> skip
    ;
