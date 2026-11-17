using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public static class NcertLibraryCatalog
{
    public static readonly IReadOnlyList<NcertBookItem> AllBooks =
    [
        // ======================== CLASS 12 ========================
        new NcertBookItem
        {
            Code = "leph1",
            Title = "Physics Part-I",
            ShortTitle = "Physics I",
            ClassNumber = 12,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Electrostatics, Current Electricity, Magnetism, and Electromagnetic Induction.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~38 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "leph2",
            Title = "Physics Part-II",
            ShortTitle = "Physics II",
            ClassNumber = 12,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Ray & Wave Optics, Dual Nature of Radiation, Atoms, Nuclei, and Semiconductors.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~35 MB",
            TotalChapters = 6
        },
        new NcertBookItem
        {
            Code = "lech1",
            Title = "Chemistry Part-I",
            ShortTitle = "Chemistry I",
            ClassNumber = 12,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Solutions, Electrochemistry, Chemical Kinetics, and Coordination Compounds.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~42 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "lech2",
            Title = "Chemistry Part-II",
            ShortTitle = "Chemistry II",
            ClassNumber = 12,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Haloalkanes, Alcohols, Aldehydes, Amines, and Biomolecules.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~40 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "lebo1",
            Title = "Biology",
            ShortTitle = "Biology",
            ClassNumber = 12,
            Subject = "Biology",
            SubjectCategory = "Science",
            Description = "Reproduction, Genetics, Evolution, Biotechnology, and Ecology.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~46 MB",
            TotalChapters = 13
        },
        new NcertBookItem
        {
            Code = "lemh1",
            Title = "Mathematics Part-I",
            ShortTitle = "Mathematics I",
            ClassNumber = 12,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Relations & Functions, Inverse Trig, Matrices, Determinants, and Calculus.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~32 MB",
            TotalChapters = 6
        },
        new NcertBookItem
        {
            Code = "lemh2",
            Title = "Mathematics Part-II",
            ShortTitle = "Mathematics II",
            ClassNumber = 12,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Integrals, Differential Equations, Vector Algebra, 3D Geometry, and Probability.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~28 MB",
            TotalChapters = 7
        },
        new NcertBookItem
        {
            Code = "lehs1",
            Title = "Themes in Indian History Part-I",
            ShortTitle = "History I",
            ClassNumber = 12,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Harappan Civilization, Early States & Economies, Kinship, Caste & Class.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~24 MB",
            TotalChapters = 4
        },
        new NcertBookItem
        {
            Code = "lehs2",
            Title = "Themes in Indian History Part-II",
            ShortTitle = "History II",
            ClassNumber = 12,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Bhakti-Sufi Traditions, Vijayanagara Empire, Agrarian Society, and Mughal Court.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~26 MB",
            TotalChapters = 4
        },
        new NcertBookItem
        {
            Code = "lehs3",
            Title = "Themes in Indian History Part-III",
            ShortTitle = "History III",
            ClassNumber = 12,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Colonialism, 1857 Revolt, Mahatma Gandhi & National Movement, and Framing the Constitution.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~28 MB",
            TotalChapters = 4
        },
        new NcertBookItem
        {
            Code = "legy1",
            Title = "Fundamentals of Human Geography",
            ShortTitle = "Human Geography",
            ClassNumber = 12,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "World Population, Human Development, Primary/Secondary Activities, and Transport.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~22 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "legy2",
            Title = "India: People and Economy",
            ShortTitle = "India Geography",
            ClassNumber = 12,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Population Distribution, Land Resources, Water, Minerals, and Planning in India.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~25 MB",
            TotalChapters = 9
        },
        new NcertBookItem
        {
            Code = "leps1",
            Title = "Contemporary World Politics",
            ShortTitle = "World Politics",
            ClassNumber = 12,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "End of Bipolarity, Contemporary Centers of Power, South Asia, UN, and Globalization.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~20 MB",
            TotalChapters = 7
        },
        new NcertBookItem
        {
            Code = "leps2",
            Title = "Politics in India Since Independence",
            ShortTitle = "Polity India",
            ClassNumber = 12,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Nation Building, Era of One-Party Dominance, Democratic Order, and Regional Aspirations.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~24 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "leec1",
            Title = "Introductory Microeconomics",
            ShortTitle = "Microeconomics",
            ClassNumber = 12,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "Consumer Behaviour, Production, Costs, and Market Equilibrium.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~18 MB",
            TotalChapters = 6
        },
        new NcertBookItem
        {
            Code = "leec2",
            Title = "Introductory Macroeconomics",
            ShortTitle = "Macroeconomics",
            ClassNumber = 12,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "National Income Accounting, Money and Banking, Government Budget, and Open Economy.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~20 MB",
            TotalChapters = 6
        },
        new NcertBookItem
        {
            Code = "lefl1",
            Title = "Flamingo (English Core)",
            ShortTitle = "Flamingo",
            ClassNumber = 12,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Prose and poetry selections for CBSE & competitive exams English prep.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~15 MB",
            TotalChapters = 14
        },
        new NcertBookItem
        {
            Code = "levt1",
            Title = "Vistas (English Supplementary)",
            ShortTitle = "Vistas",
            ClassNumber = 12,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Supplementary reader containing engaging stories and analytical essays.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~18 MB",
            TotalChapters = 6
        },

        // ======================== CLASS 11 ========================
        new NcertBookItem
        {
            Code = "keph1",
            Title = "Physics Part-I",
            ShortTitle = "Physics I",
            ClassNumber = 11,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Units, Kinematics, Laws of Motion, Work Energy Power, and Gravitation.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~36 MB",
            TotalChapters = 7
        },
        new NcertBookItem
        {
            Code = "keph2",
            Title = "Physics Part-II",
            ShortTitle = "Physics II",
            ClassNumber = 11,
            Subject = "Physics",
            SubjectCategory = "Science",
            Description = "Mechanical Properties of Solids/Fluids, Thermodynamics, and Waves & Oscillations.",
            BadgeColorHex = "#7C3AED",
            EstimatedSizeDisplay = "~34 MB",
            TotalChapters = 7
        },
        new NcertBookItem
        {
            Code = "kech1",
            Title = "Chemistry Part-I",
            ShortTitle = "Chemistry I",
            ClassNumber = 11,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Structure of Atom, Periodic Classification, Chemical Bonding, and Thermodynamics.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~38 MB",
            TotalChapters = 4
        },
        new NcertBookItem
        {
            Code = "kech2",
            Title = "Chemistry Part-II",
            ShortTitle = "Chemistry II",
            ClassNumber = 11,
            Subject = "Chemistry",
            SubjectCategory = "Science",
            Description = "Equilibrium, Redox Reactions, Organic Chemistry Basics, and Hydrocarbons.",
            BadgeColorHex = "#D97706",
            EstimatedSizeDisplay = "~36 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "kebo1",
            Title = "Biology",
            ShortTitle = "Biology",
            ClassNumber = 11,
            Subject = "Biology",
            SubjectCategory = "Science",
            Description = "Living World, Cell Biology, Plant Physiology, and Human Physiology.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~52 MB",
            TotalChapters = 19
        },
        new NcertBookItem
        {
            Code = "kemh1",
            Title = "Mathematics",
            ShortTitle = "Mathematics",
            ClassNumber = 11,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Sets, Complex Numbers, Permutations, Binomial Theorem, and Conic Sections.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~35 MB",
            TotalChapters = 14
        },
        new NcertBookItem
        {
            Code = "kehs1",
            Title = "Themes in World History",
            ShortTitle = "World History",
            ClassNumber = 11,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Early Empires, Nomadic Empires, Changing Cultural Traditions, and Industrial Revolution.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~30 MB",
            TotalChapters = 7
        },
        new NcertBookItem
        {
            Code = "kegy1",
            Title = "Fundamentals of Physical Geography",
            ShortTitle = "Physical Geography",
            ClassNumber = 11,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Earth's Origin, Landforms, Climate, Water, and Biodiversity.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~26 MB",
            TotalChapters = 14
        },
        new NcertBookItem
        {
            Code = "kegy2",
            Title = "India: Physical Environment",
            ShortTitle = "India Environment",
            ClassNumber = 11,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Structure and Physiography, Drainage Systems, Climate, Soils, and Natural Vegetation.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~24 MB",
            TotalChapters = 6
        },
        new NcertBookItem
        {
            Code = "keps1",
            Title = "Indian Constitution at Work",
            ShortTitle = "Constitution at Work",
            ClassNumber = 11,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Fundamental Rights, Elections, Executive, Legislature, Judiciary, and Federalism.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~22 MB",
            TotalChapters = 10
        },
        new NcertBookItem
        {
            Code = "keps2",
            Title = "Political Theory",
            ShortTitle = "Political Theory",
            ClassNumber = 11,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Freedom, Equality, Social Justice, Rights, Citizenship, and Nationalism.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~18 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "keec1",
            Title = "Indian Economic Development",
            ShortTitle = "Indian Economy",
            ClassNumber = 11,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "Development Policies 1947–1990, Economic Reforms, Rural Development, and Sustainable Growth.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~20 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "kehb1",
            Title = "Hornbill (English Core)",
            ShortTitle = "Hornbill",
            ClassNumber = 11,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "CBSE Class 11 core English reader with classic literature and essays.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~15 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "kesp1",
            Title = "Snapshots (English Supplementary)",
            ShortTitle = "Snapshots",
            ClassNumber = 11,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Supplementary reader with literary short stories and critical thinking selections.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~12 MB",
            TotalChapters = 5
        },

        // ======================== CLASS 10 ========================
        new NcertBookItem
        {
            Code = "jesc1",
            Title = "Science Class 10",
            ShortTitle = "Science",
            ClassNumber = 10,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Chemical Reactions, Acids Bases, Life Processes, Control & Coordination, Light, and Electricity.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~68 MB",
            TotalChapters = 13
        },
        new NcertBookItem
        {
            Code = "jemh1",
            Title = "Mathematics Class 10",
            ShortTitle = "Mathematics",
            ClassNumber = 10,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Real Numbers, Polynomials, Quadratic Equations, Triangles, Trig, and Statistics.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~31 MB",
            TotalChapters = 14
        },
        new NcertBookItem
        {
            Code = "jess1",
            Title = "Contemporary India - II (Geography)",
            ShortTitle = "Geography",
            ClassNumber = 10,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Resources & Development, Forest & Wildlife, Water, Agriculture, Minerals, and Manufacturing.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~15 MB",
            TotalChapters = 7
        },
        new NcertBookItem
        {
            Code = "jess2",
            Title = "Understanding Economic Development",
            ShortTitle = "Economics",
            ClassNumber = 10,
            Subject = "Economics",
            SubjectCategory = "Social Science",
            Description = "Development, Sectors of Indian Economy, Money & Credit, Globalization, and Consumer Rights.",
            BadgeColorHex = "#0D9488",
            EstimatedSizeDisplay = "~16 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "jess3",
            Title = "India & Contemporary World - II (History)",
            ShortTitle = "History",
            ClassNumber = 10,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "Rise of Nationalism in Europe, Nationalism in India, Making of Global World, and Print Culture.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~28 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "jess4",
            Title = "Democratic Politics - II (Civics)",
            ShortTitle = "Civics",
            ClassNumber = 10,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Power Sharing, Federalism, Gender Religion & Caste, Political Parties, and Outcomes of Democracy.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~13 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "jeff1",
            Title = "First Flight (English)",
            ShortTitle = "First Flight",
            ClassNumber = 10,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Main English prose and poetry reader for CBSE Class 10 board preparation.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~18 MB",
            TotalChapters = 9
        },
        new NcertBookItem
        {
            Code = "jefp1",
            Title = "Footprints without Feet (English)",
            ShortTitle = "Footprints",
            ClassNumber = 10,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Supplementary reader stories for CBSE Class 10 English literature.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~24 MB",
            TotalChapters = 9
        },

        // ======================== CLASS 9 ========================
        new NcertBookItem
        {
            Code = "iesc1",
            Title = "Science Class 9",
            ShortTitle = "Science",
            ClassNumber = 9,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Matter, Atoms & Molecules, Fundamental Unit of Life, Tissues, Motion, and Gravitation.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~118 MB",
            TotalChapters = 13
        },
        new NcertBookItem
        {
            Code = "iemh1",
            Title = "Ganita Manjari Part-I (Mathematics)",
            ShortTitle = "Mathematics I",
            ClassNumber = 9,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Number Systems, Polynomials, Coordinate Geometry, Linear Equations, Lines & Angles.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~19 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "iemh2",
            Title = "Ganita Manjari Part-II (Mathematics)",
            ShortTitle = "Mathematics II",
            ClassNumber = 9,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Triangles, Quadrilaterals, Circles, Heron's Formula, Surface Areas & Volumes, Statistics.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~16 MB",
            TotalChapters = 6
        },
        new NcertBookItem
        {
            Code = "iest1",
            Title = "Understanding Society: India & Beyond Part-I",
            ShortTitle = "Social Science I",
            ClassNumber = 9,
            Subject = "Social Science",
            SubjectCategory = "Social Science",
            Description = "History and Geography of India and the world under the updated NEP curriculum.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~68 MB",
            TotalChapters = 9
        },
        new NcertBookItem
        {
            Code = "iest2",
            Title = "Understanding Society: India & Beyond Part-II",
            ShortTitle = "Social Science II",
            ClassNumber = 9,
            Subject = "Social Science",
            SubjectCategory = "Social Science",
            Description = "Democratic Politics and Economic Development under the updated NEP curriculum.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~64 MB",
            TotalChapters = 6
        },
        new NcertBookItem
        {
            Code = "iebe1",
            Title = "Beehive (English)",
            ShortTitle = "Beehive",
            ClassNumber = 9,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Main English literature reader for Class 9 prose and poetry.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~35 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "iemo1",
            Title = "Moments (English Supplementary)",
            ShortTitle = "Moments",
            ClassNumber = 9,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Engaging supplementary short stories reader for Class 9.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~22 MB",
            TotalChapters = 8
        },

        // ======================== CLASS 8 ========================
        new NcertBookItem
        {
            Code = "hesc1",
            Title = "Science Class 8",
            ShortTitle = "Science",
            ClassNumber = 8,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Crop Production, Microorganisms, Combustion & Flame, Reproduction, and Force & Pressure.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~20 MB",
            TotalChapters = 13
        },
        new NcertBookItem
        {
            Code = "hemh1",
            Title = "Mathematics Class 8",
            ShortTitle = "Mathematics",
            ClassNumber = 8,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Rational Numbers, Linear Equations, Understanding Quadrilaterals, Algebraic Expressions, and Mensuration.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~17 MB",
            TotalChapters = 13
        },
        new NcertBookItem
        {
            Code = "hess2",
            Title = "Our Pasts - III (History)",
            ShortTitle = "History",
            ClassNumber = 8,
            Subject = "History",
            SubjectCategory = "Social Science",
            Description = "From Trade to Territory, Tribals Dikus & Golden Age, 1857 and After, and Making of National Movement.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~50 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "hess3",
            Title = "Social & Political Life - III (Civics)",
            ShortTitle = "Civics",
            ClassNumber = 8,
            Subject = "Political Science",
            SubjectCategory = "Social Science",
            Description = "Indian Constitution, Secularism, Parliament & Law, Judiciary, and Marginalisation.",
            BadgeColorHex = "#9333EA",
            EstimatedSizeDisplay = "~22 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "hess4",
            Title = "Resource and Development (Geography)",
            ShortTitle = "Geography",
            ClassNumber = 8,
            Subject = "Geography",
            SubjectCategory = "Social Science",
            Description = "Resources, Land Soil Water Natural Vegetation, Agriculture, Industries, and Human Resources.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~12 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "hehd1",
            Title = "Honeydew (English)",
            ShortTitle = "Honeydew",
            ClassNumber = 8,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Class 8 textbook in English containing prose and poetry selections.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~16 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "heih1",
            Title = "It So Happened (English Supplementary)",
            ShortTitle = "It So Happened",
            ClassNumber = 8,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Supplementary English reader for Class 8.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~20 MB",
            TotalChapters = 8
        },

        // ======================== CLASS 7 ========================
        new NcertBookItem
        {
            Code = "gecu1",
            Title = "Curiosity: Science Class 7",
            ShortTitle = "Science",
            ClassNumber = 7,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Nutrition in Plants & Animals, Heat, Acids Bases & Salts, Physical & Chemical Changes, and Respiration.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~42 MB",
            TotalChapters = 12
        },
        new NcertBookItem
        {
            Code = "gegp1",
            Title = "Ganita Prakash Part-I (Mathematics)",
            ShortTitle = "Mathematics I",
            ClassNumber = 7,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Integers, Fractions & Decimals, Data Handling, Simple Equations, Lines & Angles, and Triangles.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~18 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "gegp2",
            Title = "Ganita Prakash Part-II (Mathematics)",
            ShortTitle = "Mathematics II",
            ClassNumber = 7,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Comparing Quantities, Rational Numbers, Perimeter & Area, Algebraic Expressions, and Exponents.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~21 MB",
            TotalChapters = 7
        },
        new NcertBookItem
        {
            Code = "gees1",
            Title = "Exploring Society: India & Beyond Part-I",
            ShortTitle = "Social Science I",
            ClassNumber = 7,
            Subject = "Social Science",
            SubjectCategory = "Social Science",
            Description = "Historical developments, kingdoms, geography and environment under the updated curriculum.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~80 MB",
            TotalChapters = 12
        },
        new NcertBookItem
        {
            Code = "gees2",
            Title = "Exploring Society: India & Beyond Part-II",
            ShortTitle = "Social Science II",
            ClassNumber = 7,
            Subject = "Social Science",
            SubjectCategory = "Social Science",
            Description = "Democratic institutions, markets, and social structures under the updated curriculum.",
            BadgeColorHex = "#0284C7",
            EstimatedSizeDisplay = "~47 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "gepr1",
            Title = "Poorvi: English Class 7",
            ShortTitle = "Poorvi",
            ClassNumber = 7,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Updated English reader under the National Education Policy curriculum.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~72 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "gehc1",
            Title = "Honeycomb (English)",
            ShortTitle = "Honeycomb",
            ClassNumber = 7,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Prose and poetry textbook for Class 7 CBSE English.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~16 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "geac1",
            Title = "An Alien Hand (English Supplementary)",
            ShortTitle = "Alien Hand",
            ClassNumber = 7,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Class 7 supplementary English literature reader.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~18 MB",
            TotalChapters = 7
        },

        // ======================== CLASS 6 ========================
        new NcertBookItem
        {
            Code = "fecu1",
            Title = "Curiosity: Science Class 6",
            ShortTitle = "Science",
            ClassNumber = 6,
            Subject = "Science",
            SubjectCategory = "Science",
            Description = "Exploring the Nature of Science, Food Components, Materials, Living World, and Motion.",
            BadgeColorHex = "#059669",
            EstimatedSizeDisplay = "~70 MB",
            TotalChapters = 12
        },
        new NcertBookItem
        {
            Code = "fegp1",
            Title = "Ganita Prakash: Mathematics Class 6",
            ShortTitle = "Mathematics",
            ClassNumber = 6,
            Subject = "Mathematics",
            SubjectCategory = "Mathematics",
            Description = "Patterns in Mathematics, Proper Fractions, Integers, Number Play, and Perimeter & Area.",
            BadgeColorHex = "#2563EB",
            EstimatedSizeDisplay = "~20 MB",
            TotalChapters = 10
        },
        new NcertBookItem
        {
            Code = "fees1",
            Title = "Exploring Society: India & Beyond",
            ShortTitle = "Social Science",
            ClassNumber = 6,
            Subject = "Social Science",
            SubjectCategory = "Social Science",
            Description = "Oceans and Continents, Landforms, India's Civilization, Early Kingdoms, and Grassroots Democracy.",
            BadgeColorHex = "#DC2626",
            EstimatedSizeDisplay = "~201 MB",
            TotalChapters = 14
        },
        new NcertBookItem
        {
            Code = "fepr1",
            Title = "Poorvi: English Class 6",
            ShortTitle = "Poorvi",
            ClassNumber = 6,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "New curriculum foundational reader with rich moral stories, poems, and grammar foundations.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~36 MB",
            TotalChapters = 5
        },
        new NcertBookItem
        {
            Code = "fehn1",
            Title = "Honeysuckle (English)",
            ShortTitle = "Honeysuckle",
            ClassNumber = 6,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Textbook in English for Class 6 prose and poetry.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~16 MB",
            TotalChapters = 8
        },
        new NcertBookItem
        {
            Code = "fepw1",
            Title = "A Pact with the Sun (English Supplementary)",
            ShortTitle = "Pact with Sun",
            ClassNumber = 6,
            Subject = "English",
            SubjectCategory = "Language",
            Description = "Supplementary English reader for Class 6 with timeless stories.",
            BadgeColorHex = "#E11D48",
            EstimatedSizeDisplay = "~18 MB",
            TotalChapters = 7
        }
    ];
}
