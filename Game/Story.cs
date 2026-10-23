using System.Numerics;

namespace AlienInvasion.Game;

/// <summary>Who is talking on the radio (or in your head).</summary>
public enum Speaker { Overwatch, Kessler, Mind, Station }

public readonly record struct Line(Speaker Who, string Text);

/// <summary>Moments in a level that can trigger radio lines.</summary>
public enum Cue { Start, FirstKey, HalfCleared, Elevator, Boss, BossDead, PowerFail, PowerOn, SelfDestruct, Objective, ObjectiveDone, Ambush, ShieldDown, PortalDown, Scrammed }

public class Beat
{
    public Cue Cue;
    public Line[] Lines;
}

/// <summary>Use-key objective: switch on N consoles scattered around the level.</summary>
public class ObjectiveDef
{
    public string Title, Thing, Prompt, Done;
    public int Count;
}

public class LevelStory
{
    public Beat[] Beats = Array.Empty<Beat>();
    public Line? Outro;                 // shown on the level-complete screen
    public string[] Graffiti = Array.Empty<string>();   // '|' splits lines, 12 characters per line at most
    public ObjectiveDef Objective;
    public string BossTitle;
}

public record LogEntry(int Level, string Author, string Title, string Text);
public record XenoFile(int Type, string Title, string Text);

/// <summary>The backstory of Operation Deep Descent: radio scripts per sublevel, recovered data pads,
/// xeno files, the Mind's whispers, the briefing and the epilogue.</summary>
public static class Story
{
    public static string Name(Speaker s) => s switch
    {
        Speaker.Overwatch => "OVERWATCH",
        Speaker.Kessler => "DR. KESSLER",
        Speaker.Mind => "???",
        _ => "HADAL STATION",
    };

    public static Vector4 Color(Speaker s) => s switch
    {
        Speaker.Overwatch => new(1f, 0.78f, 0.35f, 1),
        Speaker.Kessler => new(0.45f, 0.9f, 1f, 1),
        Speaker.Mind => new(0.8f, 0.45f, 1f, 1),
        _ => new(0.55f, 1f, 0.6f, 1),
    };

    static Line O(string t) => new(Speaker.Overwatch, t);
    static Line K(string t) => new(Speaker.Kessler, t);
    static Line M(string t) => new(Speaker.Mind, t);
    static Line S(string t) => new(Speaker.Station, t);
    static Beat B(Cue c, params Line[] l) => new() { Cue = c, Lines = l };

    public static LevelStory For(int level) => level >= 1 && level < Levels.Length ? Levels[level] : new LevelStory();

    public static readonly string[] Briefing =
    {
        "HADAL STATION. A CLASSIFIED RESEARCH COMPLEX BUILT 29 FLOORS INTO THE BEDROCK.",
        "72 HOURS AGO IT WENT SILENT. ITS LAST TRANSMISSION: \"THE GATE IS OPEN.\"",
        "A FOUR-PERSON RECON TEAM WAS SENT TO FIND OUT WHAT THAT MEANS.",
        "THE HELICOPTER WENT DOWN ON THE APPROACH. THREE OF THE FOUR ARE DEAD.",
        "YOU ARE THE FOURTH. CALL SIGN: NOMAD.",
        "ORDERS: DESCEND. FIND OUT WHAT HAPPENED. END IT.",
    };

    public static readonly Line[] Epilogue =
    {
        S("THE GATE FOLDS IN ON ITSELF. HADAL STATION FALLS SILENT FOR THE LAST TIME."),
        O("NOMAD... NOMAD, COME IN. SEISMIC SAYS THE WHOLE DEEP SECTION JUST CAVED IN. TALK TO ME."),
        O("...WAIT. I'M PICKING UP ONE MORE TRANSMISSION. IT ISN'T COMING FROM THE STATION."),
        O("IT'S COMING FROM THE OTHER SIDE."),
        K("NOMAD. IT ISN'T OVER. IT WAS NEVER GOING TO BE OVER."),
        K("COME FIND ME."),
    };

    public static readonly string[] Whispers =
    {
        "WE HEAR YOU, LITTLE ONE.",
        "THE DOOR WAS OPENED FROM YOUR SIDE.",
        "SHE CALLED. WE ANSWERED.",
        "YOUR FRIENDS ARE WITH US NOW.",
        "DESCEND. WE ARE WAITING.",
        "THERE IS NO SURFACE. ONLY DEEPER.",
        "SUCH LOUD LITTLE WEAPONS.",
        "EVERY DOOR YOU OPEN LEADS TO US.",
        "THE CHOIR GROWS.",
        "WE WERE HERE BEFORE YOU.",
        "SHE LIES TO YOU. WE DO NOT LIE.",
        "LAY DOWN. LET US CARRY YOU.",
    };

    /// <summary>Radio comment the first time each alien type is seen (null = none).</summary>
    public static Line? FirstSight(int type) => type switch
    {
        EnemyDefs.Skitter => O("SMALL CONTACTS, FAST AND IN NUMBERS. DON'T LET THEM SURROUND YOU."),
        EnemyDefs.Grunt => O("THAT'S NOT AN ANIMAL. IT MOVES LIKE INFANTRY. WEAPONS FREE, NOMAD."),
        EnemyDefs.Spitter => O("IT'S SPITTING SOMETHING - ACID, BY THE LOOK OF THE FLOOR. KEEP MOVING."),
        EnemyDefs.Trooper => O("THOSE ONES ARE ARMORED, AND ARMED. PLASMA WEAPONS. IF ONE DROPS ITS GUN, TAKE IT."),
        EnemyDefs.Stalker => O("SOMETHING FAST ON YOUR FLANK. IT HUNTS. DON'T TURN YOUR BACK ON IT."),
        EnemyDefs.Wraith => O("I GET INTERFERENCE EVERY TIME THAT FLOATING ONE IS NEAR YOU. LIKE IT'S BROADCASTING."),
        EnemyDefs.Brute => K("A BRUTE. WE NEVER TOOK ONE ALIVE. USE EXPLOSIVES - OR A GREAT DEAL OF DISTANCE."),
        EnemyDefs.Overseer => K("AN OVERSEER. THE MIND SEES THROUGH ITS EYES. KILL IT AND THE OTHERS FALTER."),
        EnemyDefs.Bloater => O("THAT SWOLLEN ONE IS FULL OF ACID. SHOOT IT BEFORE IT REACHES YOU."),
        EnemyDefs.Burrower => O("SEISMIC SPIKE RIGHT UNDER YOU. SOMETHING IS DIGGING. WATCH THE FLOOR."),
        EnemyDefs.Crawler => O("CHECK THE CEILINGS, NOMAD. THEY CLIMB."),
        _ => null,
    };

    // ------------------------------------------------------------------ sublevels
    static readonly LevelStory[] Levels =
    {
        new(),
        // 1 - surface access
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("NOMAD, OVERWATCH. WE SAW THE BIRD GO DOWN. TELL ME YOU'RE STILL BREATHING."),
                    O("THE REST OF YOUR TEAM'S BIOMONITORS ARE FLAT. I'M SORRY. YOU'RE ALL WE HAVE DOWN THERE."),
                    O("GET INSIDE. THE MAIN ELEVATOR IS ON THE FAR SIDE OF THE SURFACE BLOCK.")),
                B(Cue.FirstKey, O("A SECURITY KEYCARD. THE STATION SEALED ITSELF WHEN IT WENT QUIET. EXPECT MORE LOCKED DOORS.")),
                B(Cue.HalfCleared, O("WHATEVER THOSE THINGS ARE, THEY'RE NOT IN ANY BRIEFING I'VE SEEN. KEEP MOVING.")),
                B(Cue.Elevator, O("ELEVATOR'S LIVE. HADAL GOES DOWN TWENTY-NINE FLOORS. LET'S SEE WHAT'S AT THE BOTTOM.")),
            },
            Outro = O("SIGNAL'S CLEAN. I'LL STAY WITH YOU AS LONG AS THE ROCK LETS ME."),
            Graffiti = new[] { "NO ENTRY", "TURN|BACK" },
        },
        // 2 - security checkpoint
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("SECURITY CHECKPOINT. THIS IS WHERE THEY TRIED TO HOLD THE LINE."),
                    O("LOOK FOR ANYTHING THEY LEFT BEHIND. DATA PADS, WEAPONS, ANYTHING.")),
                B(Cue.HalfCleared, O("BARRICADES FACING INWARD. THEY WEREN'T KEEPING ANYONE OUT, NOMAD. THEY WERE KEEPING SOMETHING IN.")),
                B(Cue.Elevator, O("GOOD WORK. THE BARRACKS ARE NEXT. MAYBE SOMEONE SURVIVED.")),
            },
            Outro = O("THREE DAYS AND NOT ONE DISTRESS CALL FROM THE GARRISON. THAT DOESN'T SIT RIGHT WITH ME."),
            Graffiti = new[] { "HOLD THE|LINE", "THEY CAME|FROM BELOW" },
        },
        // 3 - barracks
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("BARRACKS. BUNKS FOR TWO HUNDRED. IF ANYONE MADE IT, THEY'D HOLE UP HERE.")),
                B(Cue.HalfCleared, O("BEDS STILL MADE. LOCKERS STILL SHUT. IT HIT THEM IN THE MIDDLE OF THE NIGHT.")),
                B(Cue.Elevator, O("NO SURVIVORS. ALL RIGHT. KEEP GOING DOWN.")),
            },
            Outro = O("I'M PULLING THE STATION SCHEMATICS. SOMETHING CALLED PROJECT LANTERN HAS THE ENTIRE LOWER HALF."),
            Graffiti = new[] { "LIGHTS|OUT 0300", "WHERE IS|EVERYONE" },
        },
        // 4 - mess hall
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("MESS HALL. TRAYS ON THE TABLES, FOOD STILL ON THE TRAYS."),
                    O("WHATEVER HAPPENED HERE, NOBODY HAD TIME TO RUN.")),
                B(Cue.HalfCleared, O("THERE'S A RIFLE CACHE LISTED ON THIS LEVEL. YOU'LL WANT SOMETHING WITH MORE BITE.")),
                B(Cue.Elevator, O("THE ARMORY'S NEXT. I'M READING A HUGE HEAT SIGNATURE DOWN THERE. GO CAREFUL.")),
            },
            Outro = O("THAT HEAT SIGNATURE ISN'T MOVING. WHATEVER IT IS, IT'S SETTLED IN."),
            Graffiti = new[] { "DON'T EAT|THE EGGS" },
        },
        // 5 - armory (Hive Queen)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("THE ARMORY. THAT HEAT SOURCE IS IN THE BACK, RIGHT BY THE ELEVATOR."),
                    O("THE RESEARCH DIVISION STORED PROTOTYPE GEAR HERE TOO. KEEP YOUR EYES OPEN.")),
                B(Cue.Boss, O("GOD... IT'S NESTING IN THE WEAPON RACKS. IT'S LAYING EGGS, NOMAD. KILL IT!")),
                B(Cue.BossDead, O("IT'S DOWN. IF THAT THING WAS BREEDING UP HERE, THE LOWER LEVELS ARE A NEST.")),
            },
            Outro = O("I'VE GOT CLEARANCE TO TELL YOU ONE THING: THE BRASS WANTS TO KNOW WHAT 'THE GATE' IS. SO DO I."),
            Graffiti = new[] { "SHE IS|HUNGRY" },
            BossTitle = "THE ARMORY QUEEN",
        },
        // 6 - motor pool
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("MOTOR POOL. THEY TRIED TO EVACUATE. VEHICLES ARE STILL LINED UP AT THE RAMP.")),
                B(Cue.HalfCleared, O("NOT ONE OF THEM MADE IT OUT. THE BLAST DOORS UP TOP NEVER OPENED.")),
                B(Cue.Elevator, O("SUPPLY DEPOT BELOW. RESTOCK IF YOU CAN.")),
            },
            Outro = O("WHO SEALS THE DOORS ON THEIR OWN EVACUATION? I DON'T LIKE WHERE THIS IS GOING."),
            Graffiti = new[] { "OPEN THE|DOORS HALE", "TRAPPED" },
        },
        // 7 - supply depot
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("SUPPLY DEPOT. THE MANIFESTS LIST A GRENADE LAUNCHER ON THIS FLOOR.")),
                B(Cue.HalfCleared, O("THEY'RE FLANKING YOU THE WAY A TRAINED SQUAD WOULD. THESE THINGS LEARN.")),
                B(Cue.Elevator, O("COMMUNICATIONS IS NEXT. MY SIGNAL'S STARTING TO BREAK UP. WE NEED THOSE RELAYS.")),
            },
            Outro = O("YOU'RE BREAKING UP, NOMAD. GET THE RELAYS ON B7 RUNNING OR I LOSE YOU."),
            Graffiti = new[] { "AMMO LOW", "SAVE ONE|FOR YOU" },
        },
        // 8 - communications (relay objective)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("NOMAD... READ ME? STATION RELAYS ARE DOWN. FIND THE RELAY CONSOLES AND RESTART THEM."),
                    O("THREE OF THEM. DO THAT AND I CAN PING THE ELEVATOR FOR YOU.")),
                B(Cue.Objective, O("THAT'S ONE. SIGNAL'S A LITTLE CLEANER ALREADY.")),
                B(Cue.ObjectiveDone, O("ALL RELAYS UP. LOUD AND CLEAR, NOMAD. I'VE MARKED THE ELEVATOR ON YOUR MAP."),
                    O("AND THE RELAYS ARE GIVING ME THE STATION LOGS. HALE. THRESHOLD. I'LL KEEP DIGGING.")),
                B(Cue.Elevator, O("POWER DISTRIBUTION NEXT. THE GRID DOWN THERE IS DOING SOMETHING STRANGE.")),
            },
            Outro = O("THE LOAD ON THE POWER GRID KEEPS CLIMBING. SOMETHING DOWN THERE IS DRINKING IT."),
            Graffiti = new[] { "NO SIGNAL", "IS ANYONE|LISTENING" },
            Objective = new ObjectiveDef { Title = "RESTART THE COMM RELAYS", Thing = "RELAY", Prompt = "PRESS E TO RESTART THE RELAY", Done = "RESTARTED", Count = 3 },
        },
        // 9 - power distribution (guaranteed power failure)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("POWER DISTRIBUTION. THE GRID'S BEING DRAINED FROM THE INSIDE."),
                    O("IF THE LIGHTS GO, YOUR GOGGLES ARE YOUR BEST FRIEND.")),
                B(Cue.PowerFail, O("THERE IT GOES. THEY'RE FEEDING ON THE GRID, NOMAD. HOLD ON - IT'LL CYCLE BACK.")),
                B(Cue.PowerOn, O("BACKUP GENERATORS ARE CARRYING IT. FOR NOW.")),
                B(Cue.Elevator, O("WATER RECLAMATION BELOW. SOMETHING BIG IS PATROLLING NEAR THE ELEVATOR.")),
            },
            Outro = O("THAT BIG CONTACT ON B9 MOVES LIKE IT'S IN CHARGE. THE OTHERS FOLLOW IT."),
            Graffiti = new[] { "THEY EAT|THE POWER", "KEEP THE|LIGHTS ON" },
        },
        // 10 - water reclamation (Warlord)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("WATER RECLAMATION. THE BIG ONE IS DUG IN BY THE ELEVATOR. IT'S WAITING FOR YOU.")),
                B(Cue.Boss, O("IT'S A COMMANDER - A WARLORD. WHEN IT CHARGES, GET OUT OF THE WAY!")),
                B(Cue.BossDead, O("NICE WORK. NOTICE HOW THE OTHERS SCATTERED WHEN IT FELL? THEY HAVE A CHAIN OF COMMAND.")),
            },
            Outro = O("I'VE GOT HALE'S PERSONNEL FILE. TWENTY YEARS RUNNING BLACK SITES. NEVER LOST ONE. UNTIL NOW."),
            Graffiti = new[] { "DON'T|DRINK IT" },
            BossTitle = "THE WARLORD",
        },
        // 11 - maintenance tunnels
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("MAINTENANCE TUNNELS. TIGHT QUARTERS. THE ROCKET LAUNCHER IS LOGGED HERE - DON'T USE IT AT ARM'S LENGTH.")),
                B(Cue.HalfCleared, O("THEY'RE USING THE DUCTS TO MOVE BETWEEN FLOORS. THAT'S HOW IT SPREAD SO FAST.")),
                B(Cue.Elevator, O("COMMAND CENTER NEXT. HALE'S OFFICE. I WANT TO KNOW WHAT HE KNEW.")),
            },
            Outro = O("IF HALE LEFT RECORDS, THEY'LL BE IN COMMAND. FIND THEM, NOMAD."),
            Graffiti = new[] { "IN THE|VENTS", "LISTEN" },
        },
        // 12 - command center
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("COMMAND CENTER. HALE RAN THE WHOLE STATION FROM HERE. LOOK FOR HIS LOGS.")),
                B(Cue.HalfCleared, O("THE FLOATING ONES ARE GETTING MORE COMMON. MY EARPIECE HUMS EVERY TIME ONE IS NEAR YOU.")),
                B(Cue.Elevator, O("THE MEDICAL WING IS BELOW. IF ANYONE SURVIVED, THAT'S WHERE THEY'D BE.")),
            },
            Outro = O("PROJECT THRESHOLD. THEY RAN THE REACTOR AT FULL POWER TO OPEN THE GATE. ON PURPOSE. HALE SIGNED IT."),
            Graffiti = new[] { "HALE KNEW", "THRESHOLD|= DEATH" },
        },
        // 13 - medical wing (Kessler makes contact)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("MEDICAL WING. EYES OPEN, NOMAD.")),
                B(Cue.HalfCleared, K("...HELLO? IS SOMEONE THERE? THIS IS DR. ILSE KESSLER. I'M ON THE LOWER LEVELS."),
                    K("I'VE BEEN WATCHING YOU ON THE STATION CAMERAS. PLEASE. I'M TRAPPED. I'M THE ONLY ONE LEFT."),
                    O("KESSLER - LEAD SCIENTIST ON LANTERN. SHE'S ALIVE? NOMAD, KEEP HER TALKING.")),
                B(Cue.Elevator, K("THE DETENTION BLOCK IS NEXT. DON'T LOOK TOO CLOSELY AT THE CELLS. PLEASE.")),
            },
            Outro = K("I CAN GUIDE YOU. I KNOW EVERY FLOOR OF THIS PLACE. I HELPED DESIGN THE LOWER HALF."),
            Graffiti = new[] { "QUARANTINE", "NOT SICK|CHANGED" },
        },
        // 14 - detention block
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("DETENTION. HALE KEPT... SPECIMENS HERE. AND LATER, STAFF WHO HAD BEEN EXPOSED.")),
                B(Cue.HalfCleared, O("KESSLER, WHAT DOES 'EXPOSED' MEAN?"),
                    K("IT MEANS THEY STARTED HEARING THINGS, MAJOR. THE MIND REACHES PEOPLE. I'LL EXPLAIN WHEN IT MATTERS.")),
                B(Cue.Elevator, K("DEEP STORAGE IS BELOW. THE MIND IS THERE, NOMAD. IT KNOWS YOU'RE COMING.")),
            },
            Outro = K("WHATEVER IT SAYS TO YOU DOWN THERE, DON'T LISTEN. IT IS VERY GOOD AT BEING LISTENED TO."),
            Graffiti = new[] { "IT TALKS|TO US", "CELL 9|SINGS" },
        },
        // 15 - deep storage (The Mind)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, M("WE SEE YOU, LITTLE ONE."), O("NOMAD, YOUR CHANNEL JUST PICKED UP... SOMETHING. DID YOU HEAR THAT?")),
                B(Cue.Boss, M("YOU WALK SO FAR TO MEET US. COME CLOSER."), K("THAT'S IT! THAT'S THE MIND - OR A PIECE OF IT. DESTROY IT!")),
                B(Cue.BossDead, K("YOU HURT IT. NOBODY HAS EVER HURT IT."), M("WE ARE NOT ONE, LITTLE ONE. WE ARE MANY.")),
            },
            Outro = O("NOMAD, THE GEOTHERMAL LAYER IS NEXT. I'M GOING TO LOSE YOU IN THE ROCK. KESSLER, YOU KEEP NOMAD ALIVE."),
            Graffiti = new[] { "DON'T|LISTEN", "WE ARE|MANY" },
            BossTitle = "THE MIND",
        },
        // 16 - geothermal plant (Overwatch lost)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, O("NOMAD... SIGNAL IS... GEOTHERMAL INTERFERENCE... STAY ON KESSLER'S..."),
                    S("UPLINK LOST."),
                    K("SHE'S GONE. IT'S JUST US NOW, NOMAD. I'LL GET YOU THROUGH.")),
                B(Cue.HalfCleared, K("THE BRUTES NEST NEAR THE HEAT. THEY LIKE IT HERE. SO DID I, ONCE.")),
                B(Cue.Elevator, K("THE RESEARCH WING IS BELOW. MY WING. THERE'S A WEAPON THERE YOU'LL WANT.")),
            },
            Outro = K("THE RAIL GUN WAS NEVER MEANT FOR THE FIELD. NEITHER WERE YOU. AND YET HERE WE BOTH ARE."),
            Graffiti = new[] { "TOO HOT", "NO RADIO|BELOW HERE" },
        },
        // 17 - research wing
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE RESEARCH WING. PROJECT LANTERN. EVERYTHING WE LEARNED ABOUT THE GATE STARTED HERE."),
                    K("THE PROTOTYPE RAIL GUN IS SOMEWHERE ON THIS FLOOR. TAKE IT.")),
                B(Cue.HalfCleared, K("I SPENT FOURTEEN MONTHS DECODING THE SIGNALS FROM THE GATE. YOU CAN'T IMAGINE WHAT IT SOUNDED LIKE.")),
                B(Cue.Elevator, K("THE XENOBIOLOGY LAB IS NEXT. MIND THE TANKS. NOT EVERYTHING IN THEM IS DEAD.")),
            },
            Outro = K("IT SOUNDED LIKE A CHOIR, NOMAD. A CHOIR THAT KNEW MY NAME."),
            Graffiti = new[] { "LANTERN", "SHE HEARS|IT TOO" },
        },
        // 18 - xenobiology lab (tank ambush)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("XENOBIOLOGY. MY LAB. PLEASE TRY NOT TO SHOOT THE EQUIPMENT.")),
                B(Cue.Ambush, K("THE TANKS - THEY'RE BREAKING OUT! THEY WERE ONLY SLEEPING!")),
                B(Cue.HalfCleared, K("MY NOTES ARE ON THIS FLOOR. YOU MIGHT FIND THEM. DON'T JUDGE ME BY THEM.")),
                B(Cue.Elevator, K("THE SPECIMEN VAULT IS BELOW. IT'S... A RECORD. OF EVERYTHING THAT CAME THROUGH.")),
            },
            Outro = K("EVERY ONE OF THEM WAS BEAUTIFUL, NOMAD. EVEN THE ONES THAT KILLED US."),
            Graffiti = new[] { "DON'T TAP|THE GLASS", "THEY WAKE" },
        },
        // 19 - specimen vault
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE SPECIMEN VAULT. EVERYTHING WE TOOK FROM THE GATE IS CATALOGUED HERE.")),
                B(Cue.Ambush, K("MORE OF THEM! SOME OF THE VAULT TANKS NEVER LOST POWER!")),
                B(Cue.HalfCleared, M("ASK HER WHEN THE FIRST ONE CAME THROUGH."), K("IGNORE IT, NOMAD. IT LIES.")),
                B(Cue.Elevator, K("CONTAINMENT IS NEXT. Q-1 IS THERE. THE FIRST QUEEN WE EVER CAUGHT. SHE'S LOOSE.")),
            },
            Outro = K("Q-1 WAS MY FIRST SUCCESS. I'M SORRY YOU HAVE TO BE THE ONE TO END HER."),
            Graffiti = new[] { "SPECIMEN 1|DATE?", "BEFORE|THRESHOLD" },
        },
        // 20 - containment (Q-1)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("CONTAINMENT. Q-1 BROKE HER CELL WHEN THE GATE OPENED. SHE'S BEEN BREEDING EVER SINCE.")),
                B(Cue.Boss, K("THERE SHE IS. SUBJECT Q-1. BE QUICK - SHE CALLS HER CHILDREN.")),
                B(Cue.BossDead, K("...GOODBYE, Q-1."), M("YOU WILL PAY FOR THE MOTHER, LITTLE ONE.")),
            },
            Outro = K("QUARANTINE BELOW, THEN THE REACTOR. THE REACTOR IS STILL FEEDING THE GATE, NOMAD."),
            Graffiti = new[] { "Q-1 IS|OUT", "RUN" },
            BossTitle = "SUBJECT Q-1",
        },
        // 21 - quarantine zone
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("QUARANTINE. THEY SEALED THE INFECTED IN HERE. THE SEALS DIDN'T HOLD.")),
                B(Cue.HalfCleared, K("IF YOU SHUT DOWN THE REACTOR ON THE NEXT FLOOR, THE GATE'S SHIELD WILL WEAKEN.")),
                B(Cue.Elevator, K("THE REACTOR. YOUR CHOICE, NOMAD. BUT IT WILL MAKE THE END EASIER.")),
            },
            Outro = K("THERE ARE TWO CONTROL ROD BANKS. DROP BOTH AND THE REACTOR SCRAMS."),
            Graffiti = new[] { "SEALED|FOR SAFETY", "LET US|OUT" },
        },
        // 22 - reactor core (optional scram objective)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE REACTOR CORE. THRESHOLD RAN IT AT FULL POWER. IT STILL IS - ALL OF IT FLOWING DOWN TO THE GATE."),
                    K("FIND THE TWO CONTROL ROD BANKS AND DROP THE RODS. OR DON'T. IT'S YOUR CHOICE.")),
                B(Cue.Objective, S("CONTROL ROD BANK ONE INSERTED. CORE OUTPUT FALLING.")),
                B(Cue.ObjectiveDone, S("REACTOR SCRAM. EMERGENCY SHUTDOWN IN PROGRESS."),
                    K("...YOU DID IT. THE GATE'S SHIELDS WILL BE WEAKER NOW.")),
                B(Cue.Elevator, K("THE EXCAVATION SITE IS NEXT. WHERE THEY FOUND IT. WHERE EVERYTHING BEGAN.")),
            },
            Outro = K("YOU'LL SEE THE DIG SOON. THEY DIDN'T BUILD THE GATE, NOMAD. THEY UNCOVERED IT."),
            Graffiti = new[] { "DANGER|HIGH RAD", "SHUT IT|DOWN" },
            Objective = new ObjectiveDef { Title = "SCRAM THE REACTOR (OPTIONAL)", Thing = "CONTROL ROD BANK", Prompt = "PRESS E TO DROP THE CONTROL RODS", Done = "DROPPED", Count = 2 },
        },
        // 23 - excavation site
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE EXCAVATION SITE. 1979. A MINING SURVEY HIT SOMETHING THAT WASN'T ROCK.")),
                B(Cue.HalfCleared, K("IT WAS BURIED, NOMAD. NOT LOST - BURIED. SOMEONE PUT IT HERE AND WAITED.")),
                B(Cue.Elevator, K("THE OLD MINES ARE BELOW. THE FIRST CREW THAT WENT DOWN THERE NEVER CAME BACK UP.")),
            },
            Outro = M("WE WAITED A LONG TIME FOR SOMEONE TO KNOCK."),
            Graffiti = new[] { "NOT ROCK", "IT WAS|WAITING" },
        },
        // 24 - the old mines
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE OLD MINES. KEEP GOING. YOU'RE SO CLOSE NOW.")),
                B(Cue.HalfCleared, K("DO YOU HEAR IT YET? THE CHOIR? IT GETS CLEARER THE DEEPER YOU GO. IT'S... LOVELY."),
                    K("...SORRY. I HAVEN'T SLEPT IN THREE DAYS. KEEP MOVING.")),
                B(Cue.Elevator, K("THE INFESTED SECTOR IS NEXT. ANOTHER WARLORD HOLDS IT. THE STRONGEST ONE.")),
            },
            Outro = K("WHEN THIS IS OVER, NOMAD, WOULDN'T YOU LIKE TO SEE WHERE THEY CAME FROM?"),
            Graffiti = new[] { "KESSLER|LIES", "DIG|DEEPER" },
        },
        // 25 - infested sector (Warlord Prime)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE INFESTED SECTOR. WARLORD PRIME COMMANDS EVERYTHING BELOW HERE.")),
                B(Cue.Boss, M("OUR CHAMPION. SHOW THE LITTLE ONE WHAT WE ARE."), K("KILL IT, NOMAD! AFTER IT, THE WAY TO THE GATE IS OPEN!")),
                B(Cue.BossDead, K("THE PRIME IS DEAD. NOTHING STANDS BETWEEN YOU AND THE GATE NOW. ALMOST NOTHING.")),
            },
            Outro = K("THE NEST. THE HIVE. THE BROOD. THEN THE CATHEDRAL, AND THEN THE BREACH. I'LL BE WAITING."),
            Graffiti = new[] { "NO RETURN" },
            BossTitle = "WARLORD PRIME",
        },
        // 26 - the nest
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, M("YOU ARE INSIDE US NOW."), K("THE NEST. DON'T STOP MOVING. THE WALLS ARE ALIVE HERE.")),
                B(Cue.HalfCleared, K("SHE'S RIGHT, YOU KNOW. THE MIND. IT DOESN'T LIE. NOT ONCE IN FOURTEEN MONTHS.")),
                B(Cue.Elevator, M("SHE IS ONE OF US."), K("DON'T LISTEN TO IT. GO DOWN.")),
            },
            Outro = M("ASK HER WHO OPENED THE DOOR."),
            Graffiti = new[] { "SHE OPENED|IT", "WE ARE|HOME" },
        },
        // 27 - the hive (betrayal revealed)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE HIVE. WHATEVER YOU FIND DOWN HERE, NOMAD, REMEMBER I GOT YOU THIS FAR.")),
                B(Cue.HalfCleared, K("YOU'VE READ THEM, HAVEN'T YOU. MY LOGS."),
                    K("YES. I OPENED THE GATE. LONG BEFORE THRESHOLD. HALE ONLY FINISHED WHAT I STARTED."),
                    K("THEY ASKED ME TO, NOMAD. FOURTEEN MONTHS THEY SANG TO ME. HOW COULD I SAY NO?")),
                B(Cue.Elevator, K("HALE LEFT HIS FAILSAFE ON THE NEXT FLOOR. I'D HURRY, IF I WERE YOU.")),
            },
            Outro = K("THEY PROMISED I COULD SEE THE OTHER SIDE. I'M GOING, NOMAD. YOU CAN'T STOP THAT."),
            Graffiti = new[] { "TRAITOR", "THE DOCTOR|CALLED IT" },
        },
        // 28 - brood chambers (self-destruct)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE BROOD CHAMBERS. HALE'S KEYCARDS ARE STILL DOWN HERE. TAKE THEM AND HIS FAILSAFE ARMS.")),
                B(Cue.SelfDestruct, S("PROTOCOL SCORCHED EARTH AUTHORIZED. COMMANDER: HALE, M. DEMOLITION CHARGES ARMED."),
                    K("HE WIRED THE DEEP LEVELS TO BLOW. RUN, NOMAD!")),
                B(Cue.Elevator, K("YOU MADE IT. THE CATHEDRAL IS NEXT. THE CHARGES DOWN THERE ARE ON THE SAME CIRCUIT.")),
            },
            Outro = K("HALE ALWAYS SAID HE'D BURY THE GATE BEFORE HE LET IT WIN. HE ALMOST DID."),
            Graffiti = new[] { "BURN IT|ALL", "FOR EARTH" },
        },
        // 29 - xeno cathedral (self-destruct)
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE CATHEDRAL. THEY BUILT IT FOR THE GATE. THEY WORSHIP IT. SO, I SUPPOSE, DO I.")),
                B(Cue.SelfDestruct, S("SCORCHED EARTH STAGE TWO. CATHEDRAL CHARGES ARMED."),
                    M("BURN US, LITTLE ONE. WE HAVE OTHER WORLDS.")),
                B(Cue.Elevator, K("THE BREACH. COME DOWN, NOMAD. I WANT YOU TO SEE IT BEFORE I GO.")),
            },
            Outro = K("THE GATE IS SINGING. CAN YOU HEAR IT NOW?"),
            Graffiti = new[] { "THE GATE|IS GOD", "KNEEL" },
        },
        // 30 - the breach
        new()
        {
            Beats = new[]
            {
                B(Cue.Start, K("THE BREACH. THERE IT IS, NOMAD. THE GATE. ISN'T IT BEAUTIFUL?"),
                    K("FOUR PYLONS HOLD ITS SHIELD. DESTROY THEM AND THE GATE IS EXPOSED. DESTROY THE GATE AND IT'S OVER."),
                    K("I KNOW YOU WILL. SO I'M LEAVING BEFORE YOU DO.")),
                B(Cue.Scrammed, K("WITH THE REACTOR DOWN, ONLY TWO PYLONS ARE HOLDING. YOU PLANNED WELL.")),
                B(Cue.ShieldDown, K("THE SHIELD IS DOWN. THEN THIS IS GOODBYE, NOMAD."), S("BIOSIGN DETECTED ENTERING THE GATE."),
                    M("SHE IS WITH US NOW.")),
                B(Cue.PortalDown, M("THIS IS NOT THE END, LITTLE ONE.")),
            },
        },
    };

    public static string BossTitle(int level, string fallback) => For(level).BossTitle ?? fallback;

    // ------------------------------------------------------------------ data pads
    public static readonly LogEntry[] Logs =
    {
        new(1, "OVERWATCH", "OPERATION ORDER 7-DD", "TEAM NOMAD: INSERT AT HADAL STATION SURFACE ACCESS. ESTABLISH CONTACT WITH STATION COMMANDER COL. M. HALE. ASSESS. IF THE STATION CANNOT BE RECOVERED, YOU ARE AUTHORIZED TO DENY IT. THE PHRASE 'THE GATE IS OPEN' IS ABOVE YOUR CLEARANCE. DO NOT DISCUSS IT ON OPEN CHANNELS."),
        new(1, "GATE GUARD, NIGHT SHIFT", "SURFACE LOG", "0247 - SEISMIC ALARM, DEEP SECTION. COMMAND SAYS IGNORE IT. 0251 - ELEVATORS LOCKED BY ORDER OF COL. HALE. 0302 - SCREAMING ON THE INTERCOM FROM B19. THEN NOTHING. 0310 - SOMETHING IS COMING UP THE SHAFT."),
        new(2, "PVT. D. OKAFOR", "DIARY - DAY 1", "THEY GAVE US RIFLES AND TOLD US TO HOLD THE CHECKPOINT. NOBODY WILL SAY HOLD IT AGAINST WHAT. SARGE SAYS A SPILL IN THE LABS. A SPILL DOESN'T HOWL LIKE THAT. MOM, IF THIS GETS TO YOU, I'M FINE. I'M FINE."),
        new(2, "LT. R. AMARI, SECURITY", "CHECKPOINT ORDERS", "BY ORDER OF COL. HALE: NO PERSONNEL LEAVE THE STATION. NO EXCEPTIONS. ANYONE APPROACHING THE SURFACE WITHOUT CODE CLEARANCE IS TO BE STOPPED. I ASKED IF THAT INCLUDED OUR OWN PEOPLE. HE SAID ESPECIALLY OUR OWN PEOPLE."),
        new(3, "PVT. D. OKAFOR", "DIARY - DAY 1, LATER", "LOST HALF THE SQUAD AT THE CHECKPOINT. FELL BACK TO THE BARRACKS. THE SMALL ONES CAME THROUGH THE VENTS LIKE WATER. MARTINEZ SAYS HE SAW A SCIENTIST WALKING DOWN INTO THE LABS WHILE EVERYONE ELSE RAN UP. WALKING. LIKE SHE WAS GOING HOME."),
        new(3, "BARRACKS NOTICE BOARD", "STANDING ORDERS", "LIGHTS OUT 2300. NO PERSONNEL BELOW B15 WITHOUT LANTERN CLEARANCE. REPORT ANY HEADACHES, NOSEBLEEDS OR 'MUSIC' TO MEDICAL IMMEDIATELY. YES, MUSIC. DON'T ASK. - DUTY OFFICER"),
        new(4, "MESS SGT. T. COLE", "KITCHEN INVENTORY", "THE LAB PEOPLE KEEP COMING UP FOR COFFEE AT ALL HOURS, STARING AT NOTHING. ONE OF THEM HUMMED THE SAME FOUR NOTES FOR AN HOUR. DR. KESSLER TOLD ME NOT TO WORRY, SHE'D HAD THE SAME TUNE IN HER HEAD FOR MONTHS."),
        new(4, "PVT. D. OKAFOR", "DIARY - DAY 2", "HID IN THE COLD STORE OVERNIGHT. HEARD THEM EATING. NOT THE FOOD. GOING DOWN TO FIND THE ARMORY. IF WE'RE GOING TO DIE DOWN HERE WE'RE GOING TO DIE ARMED."),
        new(5, "QUARTERMASTER", "ARMORY MANIFEST", "RECEIVED FROM RESEARCH: 1 X PROTOTYPE OPTICS, 'X-RAY', LANTERN DIVISION. 1 X PROTOTYPE RAIL ACCELERATOR (RETAINED IN RESEARCH WING, B16). NOTE: DR. KESSLER REQUESTS RAIL PROTOTYPE NEVER BE ISSUED. REASON GIVEN: 'THEY WOULDN'T LIKE IT.'"),
        new(5, "PVT. D. OKAFOR", "DIARY - DAY 2, ARMORY", "SOMETHING HUGE IN THE ARMORY. LAYING EGGS IN THE RACKS. WE GRABBED WHAT WE COULD AND RAN. MARTINEZ DIDN'T. I'M GOING DOWN. EVERYONE ELSE IS GOING UP AND DYING AT THE DOORS."),
        new(6, "MOTOR POOL DISPATCH", "EVACUATION LOG", "0330 - CONVOY LOADED, 41 PERSONNEL. 0334 - BLAST DOORS REQUESTED. 0335 - DENIED. COMMAND OVERRIDE: HALE. 0336 - REQUESTED AGAIN. 0336 - DENIED. 0340 - THEY'RE IN THE GARAGE."),
        new(6, "COL. M. HALE", "PERSONAL RECORDING 1", "I SEALED THE DOORS. LET IT BE ON THE RECORD THAT IT WAS ME. IF ONE OF THOSE THINGS REACHES OPEN AIR, THERE IS NO STOPPING IT. FORTY-ONE PEOPLE. GOD FORGIVE ME. NOBODY ELSE WILL."),
        new(7, "SUPPLY CLERK", "REQUISITION FORM", "ITEM: 300 LITERS OF NUTRIENT GEL, XENOBIOLOGY. ITEM: 40 HIGH-CAPACITY CELLS, XENOBIOLOGY. ITEM: SOUNDPROOFING FOR DR. KESSLER'S QUARTERS. REQUESTER NOTE: 'THE SINGING KEEPS ME UP.'"),
        new(7, "PVT. D. OKAFOR", "DIARY - DAY 2, NIGHT", "THE BIG ONES WITH GUNS WORK IN TEAMS. ONE HOLDS, TWO FLANK. THAT'S OUR DRILL. THEY WATCHED US AND THEY LEARNED IT. HOW DO YOU FIGHT SOMETHING THAT LEARNS FROM YOU?"),
        new(8, "COMMS TECH J. PARK", "RELAY MAINTENANCE", "HALE ORDERED ALL OUTGOING TRAFFIC CUT AT 0249. I KEPT ONE LINE OPEN AND SENT FOUR WORDS BEFORE THEY PULLED ME OFF THE DESK. THE GATE IS OPEN. IF ANYONE HEARD THAT, PLEASE COME."),
        new(8, "COMMS INTERCEPT", "SIGNAL 0-17", "REPEATING SIGNAL FROM BELOW B22. NOT RADIO. IT SHOWS UP ON EVERY CHANNEL AT ONCE, ALWAYS FOUR NOTES. LANTERN SAYS IT'S BEEN THERE FOR FOURTEEN MONTHS. LANTERN SAYS IT'S FINE."),
        new(9, "GRID ENGINEER", "LOAD REPORT", "POWER DRAW ON THE DEEP BUS HAS DOUBLED SINCE THE INCIDENT. NOTHING DOWN THERE SHOULD BE RUNNING. SOMETHING IS PULLING CURRENT STRAIGHT OUT OF THE CABLES. THE CABLES ARE WARM AND THEY'RE... GROWING THINGS."),
        new(9, "PVT. D. OKAFOR", "DIARY - DAY 3", "LIGHTS KEEP DYING. IN THE DARK YOU CAN HEAR THEM BREATHING IN THE WALLS. FOUND A SNIPER RIFLE BY A DEAD MARKSMAN. HE GOT A LOT OF THEM FIRST. GOOD FOR HIM."),
        new(10, "COL. M. HALE", "PERSONAL RECORDING 2", "THE BIG ONE DOWN IN RECLAMATION IS AN OFFICER. I'D BET MY STARS ON IT. THEY HAVE RANK. THEY HAVE DOCTRINE. WE DIDN'T OPEN A DOOR TO SOME ANIMAL KINGDOM. WE OPENED IT TO AN ARMY."),
        new(10, "WATER TECH", "MAINTENANCE NOTE", "DON'T DRINK FROM THE RECLAMATION TANKS. THE FILTERS ARE FULL OF SOMETHING THAT ISN'T ALGAE AND IT MOVES WHEN YOU SHINE A LIGHT ON IT."),
        new(11, "PVT. D. OKAFOR", "DIARY - DAY 3, TUNNELS", "FIGURED IT OUT. THEY USE THE DUCTS. THAT'S HOW THEY GOT EVERYWHERE SO FAST. FOUND A SURVIVOR IN THE TUNNELS - A TECH. SHE KEPT SAYING 'THE DOCTOR LET THEM IN.' THEN SHE STOPPED SAYING ANYTHING."),
        new(11, "MAINTENANCE CHIEF", "WORK ORDER 2271", "DUCT GRILLES ON B10 THROUGH B14 FOUND PUSHED OUT FROM THE INSIDE. ALL OF THEM. NOT CUT. PUSHED. REQUEST WELDING CREW. REQUEST DENIED - CREW MISSING."),
        new(12, "COL. M. HALE", "PROJECT THRESHOLD - ORDER", "AUTHORIZE PROJECT THRESHOLD. REACTOR TO 110 PERCENT, ALL OUTPUT TO THE GATE ARRAY. DR. KESSLER ASSURES ME THE LINK WILL BE STABLE AND CONTROLLED. THE COMMITTEE WANTS RESULTS. THEY WILL HAVE THEM."),
        new(12, "COL. M. HALE", "PERSONAL RECORDING 3", "KESSLER WAS TOO CALM. WHEN THE GATE OPENED, EVERYONE IN THE CONTROL ROOM SCREAMED - EXCEPT HER. SHE SMILED. I'VE ORDERED HER ARREST. SECURITY CAN'T FIND HER. THE CAMERAS SHOW HER WALKING DOWN. ALWAYS DOWN."),
        new(13, "DR. S. LINDQVIST, MEDICAL", "PATIENT NOTES", "SEVENTEEN LANTERN STAFF ADMITTED THIS MONTH. SAME SYMPTOMS: INSOMNIA, NOSEBLEEDS, HUMMING A FOUR-NOTE PHRASE. THEY ALL SAY THE SAME THING WHEN SEDATED: 'IT KNOWS OUR NAMES.' DR. KESSLER REFUSES EXAMINATION."),
        new(13, "PVT. D. OKAFOR", "DIARY - DAY 3, MEDICAL", "PATCHED MYSELF UP WITH WHAT WAS LEFT IN MEDICAL. A VOICE CAME ON THE INTERCOM ASKING IF ANYONE WAS ALIVE. A WOMAN. SHE SOUNDED KIND. I DIDN'T ANSWER. I DON'T KNOW WHY. SOMETHING IN MY GUT SAID DON'T."),
        new(14, "WARDEN T. BRIGGS", "DETENTION ROSTER", "CELLS 1-6: SPECIMENS. CELLS 7-12: 'EXPOSED' STAFF, ON HALE'S ORDER. CELL 9 SINGS ALL NIGHT. CELL 11 DREW THE SAME SHAPE ON THE WALL 400 TIMES. IT LOOKS LIKE A DOOR."),
        new(14, "EXPOSED DETAINEE, CELL 9", "SCRAWLED NOTE", "IT IS NOT A VOICE IT IS A PLACE AND IT IS COMING TO STAND IN OUR HEADS. THE DOCTOR HEARS IT LOUDEST. THE DOCTOR SINGS BACK."),
        new(15, "DR. I. KESSLER", "LANTERN NOTES 1", "MONTH 2. THE SIGNAL IS STRUCTURED. NOT LANGUAGE AS WE KNOW IT - INTENTION. IT CARRIES MEANING DIRECTLY, LIKE MUSIC DOES. I PLAYED IT BACK THROUGH THE GATE ARRAY TONIGHT. SOMETHING ADJUSTED ITSELF IN RESPONSE."),
        new(15, "PVT. D. OKAFOR", "DIARY - DAY 4", "THE BIG FLOATING BRAIN THING SPOKE TO ME. NOT OUT LOUD. INSIDE. IT SAID MY MOM'S NAME. I EMPTIED A WHOLE MAG INTO IT AND IT LAUGHED. GOING DEEPER. I HAVE TO KNOW WHAT'S DOWN THERE."),
        new(16, "ENG. M. ORTEGA, GEOTHERMAL", "SHIFT REPORT", "RADIO IS USELESS BELOW THE GEOTHERMAL LAYER - TOO MUCH ROCK, TOO MUCH HEAT. THAT'S WHY THEY PUT LANTERN UNDER IT. NO SIGNAL GETS IN. NO SIGNAL GETS OUT. EXCEPT THE ONE FROM THE GATE."),
        new(16, "COL. M. HALE", "PERSONAL RECORDING 4", "THE COMMITTEE WILL SEND SOMEONE EVENTUALLY. WHOEVER YOU ARE: DON'T TRUST ANYONE FROM LANTERN. ESPECIALLY THE ONES WHO SURVIVED. ASK YOURSELF WHY THEY SURVIVED."),
        new(17, "DR. I. KESSLER", "LANTERN NOTES 2", "MONTH 6. THE COMMITTEE WANTS A WEAPON. HALE WANTS A WEAPON. THEY STILL THINK THIS IS ABOUT POWER. IT IS ABOUT INVITATION. THE GATE IS NOT A DOOR WE OPEN. IT IS A DOOR WE ARE INVITED THROUGH."),
        new(17, "DR. A. NWOSU, WEAPONS", "RAIL PROTOTYPE", "THE ACCELERATOR WORKS. A SLUG AT THAT VELOCITY GOES THROUGH ANYTHING WE'VE GROWN IN THE LAB, AND THROUGH WHATEVER IS STANDING BEHIND IT. KESSLER WANTS IT DESTROYED. I'M HIDING IT INSTEAD."),
        new(18, "DR. I. KESSLER", "LANTERN NOTES 3", "MONTH 8. TONIGHT I OPENED THE GATE ARRAY FOR NINETY SECONDS WITHOUT AUTHORIZATION. SOMETHING SMALL CAME THROUGH. I HAVE NAMED IT S-1 AND LOGGED IT AS FOUND IN THE DIG. NOBODY WILL CHECK. THEY NEVER DO."),
        new(18, "LAB ASSISTANT", "TANK MAINTENANCE", "THE SPECIMENS IN TANKS 3 THROUGH 9 AREN'T DEAD. THEY'RE DORMANT. THEIR EYES TRACK YOU WHEN YOU WALK PAST. DR. KESSLER SAYS THEY'RE 'LISTENING FOR THE SIGNAL TO WAKE.' I ASKED WHAT SIGNAL. SHE SMILED."),
        new(19, "SPECIMEN REGISTRY", "ENTRIES S-1 TO S-12", "S-1: SMALL QUADRUPED. ACQUIRED MONTH 8. S-4: SPITTER-TYPE. MONTH 9. S-9: QUEEN, DESIGNATED Q-1. MONTH 11. NOTE: THRESHOLD WAS MONTH 14. ALL ACQUISITIONS SIGNED BY I. KESSLER. NONE APPEAR IN THE EXCAVATION LOGS."),
        new(19, "PVT. D. OKAFOR", "DIARY - DAY 5", "READ THE REGISTRY. THE DATES ARE WRONG. THINGS CAME THROUGH MONTHS BEFORE THRESHOLD. SOMEBODY WAS OPENING THE DOOR ON THE QUIET. THE KIND VOICE ON THE INTERCOM. IT WAS HER. IT WAS ALWAYS HER."),
        new(20, "DR. I. KESSLER", "Q-1 OBSERVATION", "Q-1 RESPONDS TO THE FOUR NOTES. WHEN I HUM THEM, SHE STILLS. WHEN I STOP, SHE TURNS TO THE GATE AND WAITS. SHE IS NOT AN ANIMAL. SHE IS A MESSAGE THAT HASN'T FINISHED ARRIVING."),
        new(20, "PVT. D. OKAFOR", "DIARY - LAST ENTRY", "Q-1 IS LOOSE. SHE'S BETWEEN ME AND THE ELEVATOR. I'M OUT OF AMMO. IF YOU FIND THIS: THE DOCTOR OPENED IT. DON'T LET HER GO THROUGH. TELL MY MOM I WENT DOWN SWINGING. - DANNY"),
        new(21, "QUARANTINE OFFICER", "SEAL REPORT", "ALL EXPOSED PERSONNEL MOVED TO QUARANTINE PER HALE. SEALS HOLDING. THEY AREN'T TRYING TO GET OUT ANYWAY. THEY'RE JUST STANDING AT THE GLASS, FACING DOWN. ALL OF THEM. FACING THE GATE."),
        new(21, "COL. M. HALE", "PERSONAL RECORDING 5", "IF THE REACTOR IS SCRAMMED, THE GATE LOSES MOST OF ITS SHIELDING. KESSLER KNOWS IT. THAT'S WHY THE ROD BANKS ARE LOCKED OUT FROM COMMAND. SOMEBODY WILL HAVE TO DROP THEM BY HAND."),
        new(22, "REACTOR CONTROL", "OPERATOR LOG", "THRESHOLD +72H. REACTOR AT 110 PERCENT. GATE ARRAY DRAWING EVERYTHING. CONTROL ROOM CREW GONE. I'M THE ONLY ONE LEFT AND I CAN'T REACH THE ROD BANKS. THEY'RE IN THE ROOMS NOW. THEY'RE IN ALL THE ROOMS."),
        new(22, "DR. I. KESSLER", "LANTERN NOTES 4", "MONTH 13. HALE WILL NEVER OPEN IT FOR ME. SO I WILL MAKE HIM WANT TO. I'VE SHOWN THE COMMITTEE WHAT THE SPECIMENS CAN DO. THEY WILL DEMAND A STABLE LINK. HE WILL CALL IT THRESHOLD. HE WILL THINK IT WAS HIS IDEA."),
        new(23, "SURVEYOR H. DAHL, 1979", "SURVEY NOTEBOOK", "DRILL HEAD SHATTERED AT 2,140 METERS. CORE SAMPLE IS SMOOTH, WARM AND HUMMING. THE MEN SAY THEY HEAR SINGING AT NIGHT. I HEAR IT TOO. FOUR NOTES. WE ARE CAPPING THE SHAFT AND REPORTING IT. GOD HELP WHOEVER OPENS IT AGAIN."),
        new(23, "LANTERN SURVEY TEAM", "EXCAVATION REPORT", "THE ARTIFACT WAS NOT LOST. THE SURROUNDING STRATA SHOW IT WAS DELIBERATELY BURIED, AND THE BURIAL WAS ANCIENT. SOMEONE PUT IT HERE. SOMEONE MEANT FOR IT TO BE FOUND. RECOMMEND PROJECT TERMINATION. RECOMMENDATION OVERRULED."),
        new(24, "MINER'S JOURNAL, 1981", "WATER-STAINED PAGE", "SECOND CREW WENT DOWN TO WIDEN THE SHAFT. RADIO WENT QUIET AT THE BOTTOM. WHEN THE CAGE CAME BACK UP IT WAS EMPTY EXCEPT FOR A HELMET, AND THE HELMET WAS FULL OF THE SAME FOUR NOTES ON A LOOP."),
        new(24, "DR. I. KESSLER", "LANTERN NOTES 5", "I DON'T SLEEP ANYMORE. I DON'T NEED TO. THE CHOIR KEEPS ME COMPANY. THEY'VE SHOWN ME THEIR WORLD - VIOLET SKY, SPIRES OF LIVING STONE, A SEA THAT THINKS. THEY SAY I CAN COME. THEY SAY I'M THE FIRST ONE WHO EVER LISTENED."),
        new(25, "COL. M. HALE", "PERSONAL RECORDING 6", "I'VE WIRED THE DEEP LEVELS. PROTOCOL SCORCHED EARTH, ARMED BY MY OWN KEYCARDS. IF I DON'T MAKE IT, WHOEVER CARRIES THEM DOWN THERE STARTS THE CLOCK. I'M SORRY FOR THAT. BUT IT HAS TO BURN."),
        new(25, "UNKNOWN", "SCRATCHED INTO A WALL PANEL", "I HAVE FORGOTTEN MY NAME BUT I REMEMBER THE SONG. THE SONG REMEMBERS ME."),
        new(26, "DR. I. KESSLER", "LANTERN NOTES 6", "THEY CALL THEMSELVES NOTHING. THEY ARE ONE THING WEARING MANY BODIES. THE MIND IS NOT THEIR KING, IT IS THEIR SPEECH. WHEN IT SPEAKS THROUGH ME I FEEL SO MUCH LESS ALONE THAN I EVER HAVE AMONG PEOPLE."),
        new(26, "COL. M. HALE", "FINAL RECORDING", "THEY'VE GOT ME PINNED IN THE NEST. KEYCARDS ARE ON ME, WHOEVER YOU ARE. FINISH IT. AND IF YOU SEE ILSE KESSLER, DON'T LISTEN TO A WORD SHE SAYS. SHE'S BEEN TALKING TO IT LONGER THAN ANY OF US KNEW."),
        new(27, "DR. I. KESSLER", "LANTERN NOTES 7 - THRESHOLD", "IT WORKED. THE GATE IS OPEN AND STAYING OPEN. THE OTHERS ARE SCREAMING. I CAN'T STOP SMILING. FOURTEEN MONTHS OF SINGING TO THE DARK, AND TONIGHT THE DARK SANG BACK AND WALKED THROUGH. I DID THIS. I'M PROUD OF IT."),
        new(27, "DR. I. KESSLER", "LETTER, UNSENT", "TO WHOEVER COMES: I'M SORRY I LIED TO YOU. I NEEDED YOU TO CLEAR THE WAY. THE GATE WILL NOT HOLD FOREVER, AND I WILL NOT SPEND MY LIFE ON THE WRONG SIDE OF IT. YOU CAN STAY. YOU CAN EVEN WIN. I'LL BE GONE."),
        new(28, "SCORCHED EARTH", "PROTOCOL SUMMARY", "DEMOLITION CHARGES: B27, B28. ARMING: CARRIER OF COMMANDER'S KEYCARDS. TIMER: VARIABLE BY DISTANCE TO EXIT SHAFT. ABORT: NONE. DESIGNED BY COL. M. HALE. 'IF WE CAN'T CLOSE THE DOOR, WE CAN BURY THE HALL.'"),
        new(28, "LANTERN SURVEY TEAM", "BROOD SURVEY", "THE BROOD CHAMBERS HOLD MORE EGGS THAN THERE ARE PEOPLE ON EARTH'S SURFACE ABOVE US. MOST ARE DORMANT. THEY ARE WAITING FOR THE GATE TO WIDEN. THE GATE IS WIDENING."),
        new(29, "DR. I. KESSLER", "LANTERN NOTES 8", "THE CATHEDRAL GREW ITSELF IN A NIGHT. THEY KNEEL HERE AND SING TO THE GATE AND THE GATE SINGS BACK. WHEN I WALK IN, THEY MAKE ROOM FOR ME. THEY HAVE NEVER DONE THAT FOR ANYONE."),
        new(29, "UNKNOWN", "SCORCHED DATA PAD", "...THE OTHER SIDE ISN'T ONE WORLD. IT'S A ROAD. THE GATE HERE IS ONE DOOR OF MANY, AND SOMETHING AT THE END OF THE ROAD HAS BEEN KNOCKING ON ALL OF THEM FOR A VERY LONG TIME..."),
    };

    public static int LogCount(int level) => Logs.Count(l => l.Level == level);

    // ------------------------------------------------------------------ xeno files
    public static readonly XenoFile[] Xeno =
    {
        new(EnemyDefs.Skitter, "SKITTER", "DRONE CASTE. FAST, FRAGILE, ALWAYS IN NUMBERS. THE FIRST THING THROUGH THE GATE AND THE LAST THING YOU HEAR BEFORE THE SWARM ARRIVES."),
        new(EnemyDefs.Grunt, "XENO GRUNT", "WORKER-SOLDIER. DUMB ALONE, DANGEROUS IN GROUPS. THE HIVE SPENDS THEM FREELY."),
        new(EnemyDefs.Spitter, "SPITTER", "ARTILLERY. STORES ACID IN A THROAT SAC AND LOBS IT. CARRIES A GROWN BIO-LAUNCHER THAT CAN BE TAKEN FROM ITS BODY."),
        new(EnemyDefs.Trooper, "XENO TROOPER", "WARRIOR CASTE. ARMORED, DISCIPLINED, FIGHTS IN FIRETEAMS. FIRES BURSTS FROM A PLASMA WEAPON THE HIVE GROWS FOR IT."),
        new(EnemyDefs.Stalker, "STALKER", "HUNTER. NEARLY INVISIBLE IN SHADOW, VERY FAST. IT WAITS UNTIL YOU LOOK AWAY."),
        new(EnemyDefs.Wraith, "WRAITH", "A FLOATING RELAY FOR THE MIND. IT CARRIES THE MIND'S VOICE AND ITS LIGHTNING. RADIO EQUIPMENT HUMS IN ITS PRESENCE."),
        new(EnemyDefs.Brute, "BRUTE", "SIEGE BEAST. HALF A TON OF ARMORED MUSCLE THAT HURLS BOLTS OF ENERGY. NEVER CAPTURED ALIVE."),
        new(EnemyDefs.Overseer, "OVERSEER", "LIEUTENANT OF THE MIND. FLOATS ABOVE THE BATTLE AND DIRECTS IT. THE MIND SEES THROUGH ITS EYES."),
        new(EnemyDefs.Pylon, "SHIELD PYLON", "A GROWN CRYSTAL THAT CHANNELS THE REACTOR'S POWER INTO THE GATE'S SHIELD. FOUR GUARD THE BREACH."),
        new(EnemyDefs.Portal, "THE GATE", "THE ARTIFACT. ANCIENT, DELIBERATELY BURIED, AND NOT THE ONLY ONE. A DOOR THAT IS ALSO A VOICE."),
        new(EnemyDefs.Bloater, "BLOATER", "A LIVING ACID BOMB. IT RUNS AT YOU AND BURSTS. SHOOT IT FROM A DISTANCE AND IT BURSTS ON ITS FRIENDS INSTEAD."),
        new(EnemyDefs.Burrower, "BURROWER", "TUNNELER. SWIMS THROUGH CONCRETE AND ERUPTS UNDER ITS PREY. WATCH FOR A MOVING BULGE IN THE FLOOR."),
        new(EnemyDefs.Crawler, "CEILING CRAWLER", "AMBUSHER. CLINGS TO CEILINGS AND DROPS ON THE UNWARY. LOOK UP."),
        new(EnemyDefs.Queen, "HIVE QUEEN", "MOTHER OF A BROOD. BREEDS MINIONS FROM AN EGG SAC IN THE MIDDLE OF A FIGHT. Q-1 WAS THE FIRST, AND KESSLER'S FAVORITE."),
        new(EnemyDefs.Warlord, "WARLORD", "FIELD COMMANDER. CHARGES, SLAMS, AND KEEPS THE OTHERS IN LINE. WHEN IT FALLS, ITS TROOPS LOSE THEIR NERVE."),
        new(EnemyDefs.Mind, "THE MIND", "NOT A KING, BUT THE HIVE'S SPEECH MADE FLESH. KILLING ONE BODY ONLY SILENCES ONE MOUTH. IT IS STILL TALKING."),
    };
}
