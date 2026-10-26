/*
 * LIN Description File (LDF) grammar for ANTLR 4.
 * Supports LIN language / protocol versions 1.3, 2.0, 2.1, 2.2,
 * ISO 17987 and SAE J2602 dialects used by BUSMASTER / Vector samples.
 *
 * Sections may appear in any order after the header (real-world LDF variance).
 */
grammar Ldf;

ldfFile
    : 'LIN_description_file' ';' section* EOF
    ;

section
    : protocolVersion
    | languageVersion
    | fileRevision
    | speed
    | channelName
    | sigByteOrder
    | nodes
    | nodeComposition
    | diagnosticAddresses
    | signals
    | diagnosticSignals
    | frames
    | sporadicFrames
    | eventTriggeredFrames
    | diagnosticFrames
    | nodeAttributes
    | scheduleTables
    | signalGroups
    | signalEncodingTypes
    | signalRepresentation
    ;

protocolVersion
    : 'LIN_protocol_version' '=' STRING ';'
    ;

languageVersion
    : 'LIN_language_version' '=' STRING ';'
    ;

fileRevision
    : 'LDF_file_revision' '=' STRING ';'
    ;

speed
    : 'LIN_speed' '=' number 'kbps' ';'
    ;

channelName
    : 'Channel_name' '=' (identifier | STRING) ';'
    ;

/* ISO 17987 / LIN 2.2A optional endianness flag */
sigByteOrder
    : 'LIN_sig_byte_order_big_endian' ';'
    | 'LIN_sig_byte_order_little_endian' ';'
    ;

/* Slaves/Responders list optional. J2602:2021 uses Commander / Responders. */
nodes
    : 'Nodes' '{'
        ('Master' | 'Commander') ':' identifier ',' number 'ms' ',' number 'ms'
            (',' number 'bits' ',' number '%')?
        ';'
        (('Slaves' | 'Responders') ':' identifier (',' identifier)* ';')?
      '}'
    ;

/* LIN 1.3: NAD table (replaced by Node_attributes in 2.x) */
diagnosticAddresses
    : 'Diagnostic_addresses' '{' diagnosticAddressItem* '}'
    ;

diagnosticAddressItem
    : identifier ':' INT ';'
    ;

nodeComposition
    : 'composite' '{' configuration* '}'
    ;

configuration
    : 'configuration' identifier '{' configurationItem* '}'
    ;

configurationItem
    : identifier '{' identifier (',' identifier)* '}'
    ;

signals
    : 'Signals' '{' signalItem* '}'
    ;

signalItem
    : identifier ':' INT ',' initValue ',' identifier (',' identifier)* ';'
    ;

initValue
    : INT
    | '{' INT (',' INT)* '}'
    ;

diagnosticSignals
    : 'Diagnostic_signals' '{' diagnosticSignalItem* '}'
    ;

diagnosticSignalItem
    : identifier ':' INT ',' INT ';'
    ;

signalGroups
    : 'Signal_groups' '{' signalGroup* '}'
    ;

signalGroup
    : identifier ':' INT '{' signalGroupItem* '}'
    ;

signalGroupItem
    : identifier ',' INT ';'
    ;

frames
    : 'Frames' '{' frameItem* '}'
    ;

/* LIN 1.3 may omit frame length; 2.x usually includes it */
frameItem
    : identifier ':' INT ',' identifier (',' INT)? '{' frameSignal* '}'
    ;

frameSignal
    : identifier ',' INT ';'
    ;

sporadicFrames
    : 'Sporadic_frames' '{' sporadicFrameItem* '}'
    ;

sporadicFrameItem
    : identifier ':' identifier (',' identifier)* ';'
    ;

eventTriggeredFrames
    : 'Event_triggered_frames' '{' eventTriggeredFrameItem* '}'
    ;

eventTriggeredFrameItem
    : identifier ':' identifier ',' INT (',' identifier)* ';'
    ;

diagnosticFrames
    : 'Diagnostic_frames' '{'
        ('MasterReq' | 'CommanderReq') ':' masterId=INT '{' masterSigs+=frameSignal* '}'
        ('SlaveResp' | 'ResponderResp') ':' slaveId=INT '{' slaveSigs+=frameSignal* '}'
      '}'
    ;

nodeAttributes
    : 'Node_attributes' '{' nodeAttribute* '}'
    ;

/*
 * LIN_protocol may be a quoted string ("2.1", "ISO17987:2015", "J2602_1_1.0")
 * or a bare version number (2.0) as in older LIN 2.0 / J2602 samples.
 */
nodeAttribute
    : identifier '{'
        'LIN_protocol' '=' protocolValue ';'
        'configured_NAD' '=' INT ';'
        ('initial_NAD' '=' INT ';')?
        nodeAttributeBody*
      '}'
    ;

protocolValue
    : STRING
    | number
    ;

nodeAttributeBody
    : 'product_id' '=' INT ',' INT (',' INT)? ';'
    | 'response_error' '=' identifier ';'
    | 'fault_state_signals' '=' identifier (',' identifier)* ';'
    | 'P2_min' '=' number 'ms' ';'
    | 'ST_min' '=' number 'ms' ';'
    | 'N_As_timeout' '=' number 'ms' ';'
    | 'N_Cr_timeout' '=' number 'ms' ';'
    | 'response_tolerance' '=' number '%' ';'
    | 'wakeup_time' '=' number 'ms' ';'
    | 'poweron_time' '=' number 'ms' ';'
    | configurableFrames
    ;

configurableFrames
    : 'configurable_frames' '{' configurableFrame* '}'
    ;

configurableFrame
    : identifier ('=' INT)? ';'
    ;

scheduleTables
    : 'Schedule_tables' '{' scheduleTable* '}'
    ;

scheduleTable
    : identifier '{' scheduleCommand* '}'
    ;

scheduleCommand
    : scheduleCmd 'delay' number 'ms' ';'
    ;

/* Keyword commands first; bare frame name (identifier) last */
scheduleCmd
    : 'MasterReq'
    | 'SlaveResp'
    | 'CommanderReq'
    | 'ResponderResp'
    | 'AssignNAD' '{' identifier '}'
    | 'ConditionalChangeNAD' '{' INT ',' INT ',' INT ',' INT ',' INT ',' INT '}'
    | 'DataDump' '{' identifier ',' INT ',' INT ',' INT ',' INT ',' INT '}'
    | 'SaveConfiguration' '{' identifier '}'
    | 'AssignFrameIdRange' '{' identifier ',' INT (',' INT ',' INT ',' INT ',' INT)? '}'
    | 'FreeFormat' '{' INT ',' INT ',' INT ',' INT ',' INT ',' INT ',' INT ',' INT '}'
    | 'AssignFrameId' '{' identifier ',' identifier '}'
    | 'UnassignFrameId' '{' identifier ',' identifier '}'
    | identifier
    ;

signalEncodingTypes
    : 'Signal_encoding_types' '{' signalEncoding* '}'
    ;

signalEncoding
    : identifier '{' encodingValue* '}'
    ;

encodingValue
    : 'logical_value' ',' INT (',' STRING)? ';'
    | 'physical_value' ',' INT ',' INT ',' number ',' number (',' STRING)? ';'
    | 'bcd_value' ';'
    | 'ascii_value' ';'
    | 'float_value' ';'   /* ISO17987 / Vector float encoding */
    ;

signalRepresentation
    : 'Signal_representation' '{' signalRepresentationItem* '}'
    ;

signalRepresentationItem
    : identifier ':' identifier (',' identifier)* ';'
    ;

/* Offset / scale may be negative (e.g. physical_value ..., -40, "degree") */
number
    : '-'? INT
    | '-'? FLOAT
    ;

/*
 * Keywords are distinct lexer tokens (from '...' literals). Real LDFs reuse
 * MasterReq/SlaveResp as frame names inside Frames { }, and schedule commands
 * also use those tokens — so identifier must accept keyword tokens too.
 */
identifier
    : IDENTIFIER
    | 'MasterReq'
    | 'SlaveResp'
    | 'CommanderReq'
    | 'ResponderResp'
    | 'Master'
    | 'Commander'
    | 'Slaves'
    | 'Responders'
    | 'AssignNAD'
    | 'ConditionalChangeNAD'
    | 'DataDump'
    | 'SaveConfiguration'
    | 'AssignFrameIdRange'
    | 'FreeFormat'
    | 'AssignFrameId'
    | 'UnassignFrameId'
    ;

/* ---- Lexer ---- */

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
