import os
import itertools
import requests
import json
import pyodbc
import asyncio
from concurrent.futures import ThreadPoolExecutor
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, status
from pydantic import BaseModel, Field

try:
    from google import genai
except ImportError:
    genai = None


# ============================================================================
# DATABASE CONFIGURATION
# ============================================================================

DB_CONNECTION_STRING = (
    "DRIVER={ODBC Driver 17 for SQL Server};"
    "SERVER=localhost\\SQLEXPRESS;"
    "DATABASE=EmployeeDB;"
    "Trusted_Connection=yes;"
)


def get_db_connection():
    """Get a pyodbc connection to SQL Server (sync)."""
    try:
        conn = pyodbc.connect(DB_CONNECTION_STRING, timeout=5)
        return conn
    except Exception as e:
        print(f"[DB] Connection error: {e}")
        raise RuntimeError(f"Failed to connect to database: {e}")



app = FastAPI(title="Employee System AI Service")

# Executor for running blocking database operations
db_executor = ThreadPoolExecutor(max_workers=5)

# ============================================================================
# AI PROVIDER CONFIGURATION
# ============================================================================

# GEMINI: 3 API keys in round-robin rotation (45K requests/day total)
GEMINI_KEYS = [
    os.getenv("GEMINI_API_KEY_1", ""),
    os.getenv("GEMINI_API_KEY_2", ""),
    os.getenv("GEMINI_API_KEY_3", ""),
]
GEMINI_KEYS = [k for k in GEMINI_KEYS if k]  # Remove empty keys
_gemini_cycle = itertools.cycle(GEMINI_KEYS) if GEMINI_KEYS else None
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-2.5-flash")

# GROQ Configuration
GROQ_API_KEY = os.getenv("GROQ_API_KEY", "")
GROQ_MODEL = "meta-llama/llama-3.1-70b-versatile"
GROQ_URL = "https://api.groq.com/openai/v1/chat/completions"

# OPENROUTER Configuration  
OPENROUTER_API_KEY = os.getenv("OPENROUTER_API_KEY", "")
OPENROUTER_MODEL = "meta-llama/llama-3.1-70b-instruct:free"
OPENROUTER_URL = "https://openrouter.ai/api/v1/chat/completions"


def get_next_gemini_key():
    """Get next Gemini API key in round-robin rotation."""
    if _gemini_cycle is None:
        return None
    return next(_gemini_cycle)

# ============================================================================
# PYDANTIC MODELS
# ============================================================================

class ChatRequest(BaseModel):
    question: str
    context: str


class ChatResponse(BaseModel):
    answer: str


class SqlRequest(BaseModel):
    question: str


class SqlResponse(BaseModel):
    sql: str


class AnalysisRequest(BaseModel):
    question: str
    results: str  # JSON string of SQL results


class AnalysisResponse(BaseModel):
    analysis: str


# ============================================================================
# AI PROVIDER IMPLEMENTATIONS
# ============================================================================

def call_gemini(prompt: str) -> tuple[str, str]:
    """Call Gemini with next available API key in rotation.
    
    Returns:
        (response_text, gemini_key_used)
    """
    if genai is None:
        raise RuntimeError("google-genai package is not installed")

    api_key = get_next_gemini_key()
    if not api_key:
        raise RuntimeError("No Gemini API keys configured")

    client = genai.Client(api_key=api_key)
    response = client.models.generate_content(
        model=GEMINI_MODEL,
        contents=prompt,
    )
    
    if not response.text:
        raise RuntimeError("Gemini returned empty response")
        
    return response.text, api_key[:10] + "..."


def call_groq(prompt: str) -> tuple[str, str]:
    """Call Groq LLM service.
    
    Returns:
        (response_text, provider_name)
    """
    if not GROQ_API_KEY:
        raise RuntimeError("GROQ_API_KEY not configured")

    headers = {
        "Authorization": f"Bearer {GROQ_API_KEY}",
        "Content-Type": "application/json"
    }
    
    body = {
        "model": GROQ_MODEL,
        "messages": [{"role": "user", "content": prompt}],
        "max_tokens": 2000,
        "temperature": 0.1
    }
    
    response = requests.post(
        GROQ_URL,
        headers=headers,
        json=body,
        timeout=30
    )
    response.raise_for_status()
    
    result = response.json()
    content = result["choices"][0]["message"]["content"]
    
    if not content:
        raise RuntimeError("Groq returned empty response")
        
    return content, "Groq"


def call_openrouter(prompt: str) -> tuple[str, str]:
    """Call OpenRouter proxy service.
    
    Returns:
        (response_text, provider_name)
    """
    if not OPENROUTER_API_KEY:
        raise RuntimeError("OPENROUTER_API_KEY not configured")

    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json",
        "HTTP-Referer": "http://localhost:5194",
        "X-Title": "EmployeeSystem"
    }
    
    body = {
        "model": OPENROUTER_MODEL,
        "messages": [{"role": "user", "content": prompt}],
        "max_tokens": 2000,
        "temperature": 0.1
    }
    
    response = requests.post(
        OPENROUTER_URL,
        headers=headers,
        json=body,
        timeout=30
    )
    response.raise_for_status()
    
    result = response.json()
    content = result["choices"][0]["message"]["content"]
    
    if not content:
        raise RuntimeError("OpenRouter returned empty response")
        
    return content, "OpenRouter"


def call_ai_with_fallback(prompt: str, endpoint: str = "unknown") -> tuple[str, str]:
    """AI response handler with provider fallback chain.
    
    Tries providers in order: Gemini → Groq → OpenRouter
    Falls back to next provider on failure.
    
    Args:
        prompt: The prompt to send
        endpoint: The calling endpoint (for logging)
    
    Returns:
        (response_text, provider_used)
    
    Raises:
        RuntimeError: If all providers fail
    """
    
    # Try providers in order
    providers_to_try = [
        ("Gemini", call_gemini),
        ("Groq", call_groq),
        ("OpenRouter", call_openrouter),
    ]
    
    last_error = None
    for provider_name, provider_func in providers_to_try:
        try:
            print(f"[AI] 🔄 Trying {provider_name}...")
            response, key_info = provider_func(prompt)
            print(f"[AI] ✓ Success with {provider_name} {key_info}")
            return response, provider_name
        except RuntimeError as e:
            last_error = e
            print(f"[AI] ✗ {provider_name} failed: {e}")
            continue
        except Exception as e:
            last_error = e
            print(f"[AI] ✗ {provider_name} error: {e}")
            continue
    
    # All providers failed
    error_msg = f"All AI providers failed. Last error: {last_error}"
    print(f"[AI] ✗ {error_msg}")
    raise RuntimeError(error_msg)





def build_hr_prompt(question: str, context: str) -> str:
    return f"""You are an HR Assistant AI connected to a SQL Server HR database.
The user will ask questions in Mongolian, English, or romanized Mongolian.
Your job: understand the question and provide insightful analysis.

INFORMATION PROVIDED:
{context}

USER QUESTION:
{question}

Provide a professional HR analysis in Mongolian.
Keep it concise and actionable."""


async def build_sql_context(question: str) -> str:
    """Build context with schema and actual lookup values from database."""
    loop = asyncio.get_event_loop()
    
    def _fetch_from_db():
        departments, positions, att_statuses, leave_types, employees, projects, trainings = [], [], [], [], [], [], []
        try:
            conn = get_db_connection()
            if not conn:
                print("[DB] Failed to connect to database")
                return [], [], [], [], [], [], []
                
            cursor = conn.cursor()
            
            # Query actual lookup values from database
            try:
                cursor.execute("SELECT DepartmentName FROM Departments")
                departments = [row[0] for row in cursor.fetchall()]
                print(f"[DB] Loaded {len(departments)} departments")
            except Exception as e:
                print(f"[DB] Departments query failed: {e}")
            
            try:
                cursor.execute("SELECT PositionName FROM Positions")
                positions = [row[0] for row in cursor.fetchall()]
                print(f"[DB] Loaded {len(positions)} positions")
            except Exception as e:
                print(f"[DB] Positions query failed: {e}")
            
            try:
                cursor.execute("SELECT DISTINCT Status FROM Attendance WHERE Status IS NOT NULL")
                att_statuses = [row[0] for row in cursor.fetchall()]
                print(f"[DB] Loaded {len(att_statuses)} attendance statuses")
            except Exception as e:
                print(f"[DB] Attendance status query failed: {e}")
            
            try:
                cursor.execute("SELECT DISTINCT LeaveType FROM LeaveRequests WHERE LeaveType IS NOT NULL")
                leave_types = [row[0] for row in cursor.fetchall()]
                print(f"[DB] Loaded {len(leave_types)} leave types")
            except Exception as e:
                print(f"[DB] Leave types query failed: {e}")
            
            # Get employee names and emails for better AI understanding
            try:
                cursor.execute("SELECT TOP 20 FirstName, LastName, Email FROM Employees ORDER BY EmployeeId")
                employees = [(f"{row[0]} {row[1]}", row[2]) for row in cursor.fetchall()]
                print(f"[DB] Loaded {len(employees)} employees")
            except Exception as e:
                print(f"[DB] Employees query failed: {e}")
            
            # Get project names (optional table) - skip if not exists
            try:
                cursor.execute("SELECT ProjectName FROM Projects")
                projects = [row[0] for row in cursor.fetchall()]
                print(f"[DB] Loaded {len(projects)} projects")
            except:
                print("[DB] Projects table not found or query failed, skipping")
                projects = []
            
            # Get training names (optional table) - skip if not exists
            try:
                cursor.execute("SELECT TrainingName FROM Trainings")
                trainings = [row[0] for row in cursor.fetchall()]
                print(f"[DB] Loaded {len(trainings)} trainings")
            except:
                print("[DB] Trainings table not found or query failed, skipping")
                trainings = []
            
            cursor.close()
            conn.close()
        except Exception as e:
            print(f"[DB] Critical error in _fetch_from_db: {e}")
        
        return departments, positions, att_statuses, leave_types, employees, projects, trainings
    
    try:
        departments, positions, att_statuses, leave_types, employees, projects, trainings = await loop.run_in_executor(db_executor, _fetch_from_db)
        
        # Format employee list for context
        employee_list = ", ".join([f"{name} ({email})" for name, email in employees[:10]]) if employees else "No employees found"
        project_list = ", ".join(projects[:10]) if projects else "No projects found"
        training_list = ", ".join(trainings[:10]) if trainings else "No trainings found"
        
        context = f"""You are an AI assistant connected to a SQL Server HR database.
The user will ask questions in Mongolian, English, or romanized Mongolian.
Your job: understand the question and write a correct SQL Server SELECT query ONLY.

TABLES AND RELATIONSHIPS:
Employees        (EmployeeId PK, FirstName, LastName, DepartmentId→Departments, PositionId→Positions, Phone, Email, HireDate, IsActive)
Departments      (DepartmentId PK, DepartmentName)
Positions        (PositionId PK, PositionName)
Payroll          (PayrollId PK, EmployeeId→Employees, Salary, Bonus, Deduction, NetSalary, PayMonth)
Attendance       (AttendanceId PK, EmployeeId→Employees, AttendanceDate, CheckInTime, CheckOutTime, Status)
LeaveRequests    (LeaveRequestId PK, EmployeeId→Employees, LeaveType, StartDate, EndDate, Reason, Status, Duration)
PerformanceReviews(ReviewId PK, EmployeeId→Employees, ReviewDate, Score)
Trainings        (TrainingId PK, TrainingName, Category, StartDate, EndDate)
EmployeeTraining (EmployeeTrainingId PK, EmployeeId→Employees, TrainingId→Trainings, CompletionDate)
Projects         (ProjectId PK, ProjectName, DepartmentId→Departments, Budget, StartDate, EndDate)
EmployeeProjects (EmployeeProjectId PK, EmployeeId→Employees, ProjectId→Projects)
Assets           (AssetId PK, AssetName, AssetType, PurchaseDate, IsActive)
EmployeeAssets   (EmployeeAssetId PK, EmployeeId→Employees, AssetId→Assets)
Users            (UserId PK, Username, PasswordHash, EmployeeId→Employees)
Roles            (RoleId PK, RoleName)
UserRoles        (UserRoleId PK, UserId→Users, RoleId→Roles)
Notifications    (NotificationId PK, UserId→Users, Title, Message, IsRead)
Candidates       (CandidateId PK, FirstName, LastName, Phone, Email, Position, Status)
Interviews       (InterviewId PK, CandidateId→Candidates, InterviewDate, Feedback, Result)

CURRENT DATA IN DATABASE:
Departments: {departments}
Positions: {positions}
Attendance Status: {att_statuses}
Leave Types: {leave_types}

SAMPLE EMPLOYEES (sample only - query database for complete list):
{employee_list}

SAMPLE PROJECTS:
{project_list}

SAMPLE TRAININGS:
{training_list}

IMPORTANT NOTES FOR MONGOLIAN QUESTIONS:
- Questions may use Mongolian names like "болд", "билгүүн", "ану" - match these to FirstName and LastName columns
- Questions may ask about departments, positions, attendance, performance, projects, etc.
- Always JOIN with Employees table when filtering by employee name
- Use CASE WHEN for status-based aggregations

USER QUESTION:
{question}

INSTRUCTIONS:
1. Write ONLY a SQL Server SELECT query
2. The query must be SELECT-only (no INSERT, UPDATE, DELETE, etc.)
3. Use only tables and columns that exist in the schema
4. If joining tables, use proper ON clauses with Employees as the main table
5. Use DISTINCT, GROUP BY, aggregates as needed
6. Return ONLY the SQL query. Nothing else. No markdown, backticks, or explanation."""
        return context
        
    except Exception as e:
        print(f"[DB] Error building context: {e}")
        return f"""You are an AI SQL Server query generator for an HR database.
Write a SELECT query to answer: {question}
Return ONLY the SQL. Nothing else."""


def build_analysis_prompt(question: str, results: str) -> str:
    return f"""The user asked: "{question}"

SQL query result:
{results}

Summarize this result in 1-2 sentences in Mongolian.
Provide a professional, actionable summary.
Do not invent data beyond what the result shows."""


@app.post("/chat", response_model=ChatResponse)
def chat(request: ChatRequest):
    if not request.question.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Question is required.",
        )

    if not request.context.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Context is required.",
        )

    prompt = build_hr_prompt(request.question, request.context)

    try:
        answer, provider = call_ai_with_fallback(prompt, endpoint="chat")
        return {"answer": answer}
    except Exception as exc:
        print(f"[AI] Chat endpoint failed: {exc}")
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail=f"All AI providers failed. Please try again later.",
        ) from exc


@app.post("/table-selection", response_model=SqlResponse)
async def sql_agent(request: SqlRequest):
    """Generate SQL from question with zero hardcoded rules."""
    if not request.question.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Question is required.",
        )

    try:
        # Build context with real database lookup values
        context = await build_sql_context(request.question)
        
        # AI thinks and writes SQL
        sql, provider = call_ai_with_fallback(context, endpoint="sql-agent")
        sql = sql.strip()
        sql = sql.replace("```sql", "").replace("```", "").strip().rstrip(";")
        
        print(f"[SQL] Generated by {provider}: {sql[:100]}...")
        return {"sql": sql}
        
    except Exception as exc:
        print(f"[AI] SQL generation failed: {exc}")
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="All AI providers failed for SQL generation. Please try again.",
        ) from exc


@app.post("/sql-agent", response_model=SqlResponse)
async def sql_generation(request: SqlRequest):
    """Generate SQL from question with zero hardcoded rules."""
    if not request.question.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Question is required.",
        )

    try:
        # Build context with real database lookup values
        context = await build_sql_context(request.question)
        
        # AI thinks and writes SQL
        sql, provider = call_ai_with_fallback(context, endpoint="sql-agent")
        sql = sql.strip()
        sql = sql.replace("```sql", "").replace("```", "").strip().rstrip(";")
        
        print(f"[SQL] Generated by {provider}: {sql[:100]}...")
        return {"sql": sql}
        
    except Exception as exc:
        print(f"[AI] SQL generation failed: {exc}")
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="All AI providers failed for SQL generation. Please try again.",
        ) from exc


@app.post("/sql-analysis", response_model=AnalysisResponse)
async def sql_analysis(request: AnalysisRequest):
    """Analyze SQL result in 1-2 sentences of Mongolian."""
    try:
        prompt = build_analysis_prompt(request.question, request.results)
        analysis, provider = call_ai_with_fallback(prompt, endpoint="sql-analysis")
        print(f"[Analysis] {provider}: {analysis[:80]}...")
        return {"analysis": analysis}
    except Exception as exc:
        print(f"[AI] SQL analysis failed: {exc}")
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="All AI providers failed for analysis. Please try again.",
        ) from exc


@app.get("/status")
def status_check():
    """Check system health and configuration."""
    return {
        "status": "ok",
        "backend": "FastAPI",
        "port": 8000,
        "api_keys_configured": {
            "gemini": len(GEMINI_KEYS) > 0,
            "groq": bool(GROQ_API_KEY),
            "openrouter": bool(OPENROUTER_API_KEY)
        },
        "test_mode": any(k in str(GEMINI_KEYS) + GROQ_API_KEY + OPENROUTER_API_KEY for k in ["test-", "dummy-", "fake-"])
    }


@app.get("/insight")
def insight(
    totalEmployees: int,
    topDepartment: str,
    topPosition: str,
    newEmployees: int,
):
    context = f"""
ORGANIZATION SUMMARY
Total Employees: {totalEmployees}
Largest Department: {topDepartment}
Most Common Position: {topPosition}
New Employees In Last 30 Days: {newEmployees}
"""

    request = ChatRequest(
        question="Энэ байгууллагын ажиллах хүчний байдлыг товч мэргэжлийн дүгнэлтээр тайлбарла.",
        context=context,
    )

    return {"insight": chat(request)["answer"]}
